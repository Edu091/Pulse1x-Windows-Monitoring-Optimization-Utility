using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Pulse1x.App.Services;

/// <summary>
/// Lê SMART diretamente do disco físico via IOCTL (o mesmo mecanismo de baixo nível que
/// ferramentas como o CrystalDiskInfo usam), em vez de depender do provedor WMI
/// (MSStorageDriver_FailurePredict*), que muitos drivers modernos (AHCI/NVMe) não implementam
/// — por isso o diagnóstico só conseguia mostrar "N/D" antes desta mudança.
///
/// Todas as operações são de LEITURA (comandos ATA SMART READ DATA / RETURN STATUS, ou a
/// consulta padrão do Windows IOCTL_STORAGE_PREDICT_FAILURE). Nada é escrito no disco nem em
/// nenhuma configuração do sistema.
/// </summary>
internal static class SmartReader
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;

    // CTL_CODE(IOCTL_SCSI_BASE=4, 0x040B, METHOD_BUFFERED, FILE_READ_ACCESS|FILE_WRITE_ACCESS)
    private const uint IOCTL_ATA_PASS_THROUGH = 0x0004D02C;

    // CTL_CODE(FILE_DEVICE_MASS_STORAGE=0x2D, 0x0100, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IOCTL_STORAGE_PREDICT_FAILURE = 0x002D0400;

    // CTL_CODE(FILE_DEVICE_MASS_STORAGE=0x2D, 0x0500, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    // CTL_CODE(IOCTL_SCSI_BASE=4, 0x0405, METHOD_BUFFERED, FILE_READ_ACCESS|FILE_WRITE_ACCESS)
    private const uint IOCTL_SCSI_PASS_THROUGH_DIRECT = 0x0004D014;

    private const int AtaHeaderSize = 48; // sizeof(ATA_PASS_THROUGH_EX) em x64

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[]? lpInBuffer, uint nInBufferSize, byte[]? lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    private static extern bool DeviceIoControlPtr(
        SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    /// <summary>
    /// Lê a tabela de atributos SMART (512 bytes, mesmo layout usado pelo WMI VendorSpecific)
    /// via ATA PASS THROUGH (comando SMART READ DATA), além do veredito de saúde do próprio
    /// firmware do disco (comando SMART RETURN STATUS). Só funciona em discos ATA/SATA.
    /// </summary>
    public static bool TryReadAtaSmart(int driveIndex, out byte[] attributeTable, out bool predictFailure)
    {
        attributeTable = Array.Empty<byte>();
        predictFailure = false;

        using var handle = OpenPhysicalDrive(driveIndex, requireWrite: true);
        if (handle is null) return false;

        byte[] readBuf = BuildAtaPassThroughBuffer(feature: 0xD0, withDataPhase: true);
        bool ok = DeviceIoControl(handle, IOCTL_ATA_PASS_THROUGH, readBuf, (uint)readBuf.Length,
            readBuf, (uint)readBuf.Length, out _, IntPtr.Zero);
        if (!ok) return false;

        attributeTable = readBuf[AtaHeaderSize..];

        byte[] statusBuf = BuildAtaPassThroughBuffer(feature: 0xDA, withDataPhase: false);
        bool statusOk = DeviceIoControl(handle, IOCTL_ATA_PASS_THROUGH, statusBuf, (uint)statusBuf.Length,
            statusBuf, (uint)statusBuf.Length, out _, IntPtr.Zero);

        // Assinatura ATA8-ACS no retorno: 0x4F/0xC2 = saudável; 0xF4/0x2C = falha prevista.
        if (statusOk)
            predictFailure = statusBuf[43] == 0xF4 && statusBuf[44] == 0x2C;

        return true;
    }

    /// <summary>
    /// Lê a tabela de atributos SMART (512 bytes) de discos atrás de uma ponte USB-SATA, enviando o
    /// comando ATA "SMART READ DATA" encapsulado em SCSI (tradução SAT, ATA PASS-THROUGH 16/12) via
    /// IOCTL_SCSI_PASS_THROUGH_DIRECT — exatamente o mecanismo que o CrystalDiskInfo usa para discos
    /// USB, já que o driver USB do Windows não encaminha o IOCTL_ATA_PASS_THROUGH comum. Só funciona
    /// se a ponte do gabinete suportar SAT (muitas suportam; algumas baratas não — aí falha em paz).
    /// </summary>
    public static bool TryReadAtaSmartViaScsi(int driveIndex, out byte[] attributeTable)
    {
        attributeTable = Array.Empty<byte>();

        using var handle = OpenPhysicalDrive(driveIndex, requireWrite: true);
        if (handle is null) return false;

        // Tenta ATA PASS-THROUGH (16) e, se a ponte não aceitar, (12) — mesma estratégia do CrystalDiskInfo.
        return TryScsiSmartRead(handle, use16: true, out attributeTable)
            || TryScsiSmartRead(handle, use16: false, out attributeTable);
    }

    private static bool TryScsiSmartRead(SafeFileHandle handle, bool use16, out byte[] attributeTable)
    {
        attributeTable = Array.Empty<byte>();

        // Layout de SCSI_PASS_THROUGH_DIRECT em x64: cabeçalho de 56 bytes + buffer de sense logo após.
        const int sptdSize = 56;
        const int senseLen = 32;
        int inLen = sptdSize + senseLen;

        IntPtr inBuf = Marshal.AllocHGlobal(inLen);
        byte[] data = new byte[512];
        var dataPin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            for (int i = 0; i < inLen; i++) Marshal.WriteByte(inBuf, i, 0);

            byte[] cdb = use16 ? BuildAtaPassThrough16() : BuildAtaPassThrough12();

            Marshal.WriteInt16(inBuf, 0, sptdSize);                               // Length
            Marshal.WriteByte(inBuf, 6, (byte)cdb.Length);                        // CdbLength (16 ou 12)
            Marshal.WriteByte(inBuf, 7, senseLen);                               // SenseInfoLength
            Marshal.WriteByte(inBuf, 8, 1);                                       // DataIn = SCSI_IOCTL_DATA_IN
            Marshal.WriteInt32(inBuf, 12, 512);                                   // DataTransferLength
            Marshal.WriteInt32(inBuf, 16, 10);                                    // TimeOutValue (s)
            Marshal.WriteIntPtr(inBuf, 24, dataPin.AddrOfPinnedObject());         // DataBuffer (ponteiro real)
            Marshal.WriteInt32(inBuf, 32, sptdSize);                              // SenseInfoOffset
            for (int i = 0; i < cdb.Length; i++) Marshal.WriteByte(inBuf, 36 + i, cdb[i]); // Cdb

            bool ok = DeviceIoControlPtr(handle, IOCTL_SCSI_PASS_THROUGH_DIRECT,
                inBuf, (uint)inLen, inBuf, (uint)inLen, out _, IntPtr.Zero);
            if (!ok) return false;

            // ScsiStatus 0 = GOOD. Alguns bridges devolvem dados mesmo com status não-zero; por isso
            // validamos pelo conteúdo (HasAnyData no chamador) em vez de exigir status perfeito.
            attributeTable = data;
            // Sanidade mínima: a tabela SMART READ DATA não é toda zero num disco real.
            return Array.Exists(data, b => b != 0);
        }
        catch { return false; }
        finally
        {
            dataPin.Free();
            Marshal.FreeHGlobal(inBuf);
        }
    }

    // CDB do ATA PASS-THROUGH (16) (opcode 0x85) para "SMART READ DATA": protocolo PIO Data-In,
    // transferência de 1 setor (512 bytes) com a assinatura SMART 0x4F/0xC2.
    private static byte[] BuildAtaPassThrough16() => new byte[]
    {
        0x85,       // ATA PASS-THROUGH(16)
        0x08,       // PROTOCOL = 4 (PIO Data-In) << 1
        0x0E,       // T_DIR=1 (do disco), BYT_BLOK=1, T_LENGTH=2 (contagem em SECTOR_COUNT)
        0x00, 0xD0, // Features (15:8, 7:0) — 0xD0 = SMART READ DATA
        0x00, 0x01, // Sector count (15:8, 7:0) = 1
        0x00, 0x00, // LBA low
        0x00, 0x4F, // LBA mid  — assinatura SMART
        0x00, 0xC2, // LBA high — assinatura SMART
        0xA0,       // Device
        0xB0,       // Command = SMART
        0x00,       // Control
    };

    // CDB do ATA PASS-THROUGH (12) (opcode 0xA1) — alternativa para pontes que não aceitam o de 16.
    private static byte[] BuildAtaPassThrough12() => new byte[]
    {
        0xA1,       // ATA PASS-THROUGH(12)
        0x08,       // PROTOCOL = 4 (PIO Data-In) << 1
        0x0E,       // T_DIR=1, BYT_BLOK=1, T_LENGTH=2
        0xD0,       // Features = SMART READ DATA
        0x01,       // Sector count = 1
        0x00,       // LBA low
        0x4F,       // LBA mid
        0xC2,       // LBA high
        0xA0,       // Device
        0xB0,       // Command = SMART
        0x00,       // Reserved
        0x00,       // Control
    };

    /// <summary>
    /// Consulta genérica do Windows (funciona através de qualquer driver de armazenamento,
    /// incluindo NVMe) que devolve apenas o veredito de falha prevista, sem a tabela detalhada.
    /// </summary>
    public static bool TryReadGenericPredictFailure(int driveIndex, out bool predictFailure)
    {
        predictFailure = false;

        using var handle = OpenPhysicalDrive(driveIndex, requireWrite: false);
        if (handle is null) return false;

        var outBuf = new byte[4 + 512];
        bool ok = DeviceIoControl(handle, IOCTL_STORAGE_PREDICT_FAILURE, null, 0, outBuf, (uint)outBuf.Length,
            out _, IntPtr.Zero);
        if (!ok) return false;

        predictFailure = BitConverter.ToUInt32(outBuf, 0) != 0;
        return true;
    }

    /// <summary>
    /// Segunda via para o log de saúde NVMe: alguns drivers/controladores não respondem à
    /// consulta StorageDeviceProtocolSpecificProperty, mas devolvem o mesmo log de 512 bytes
    /// dentro do campo VendorSpecific de IOCTL_STORAGE_PREDICT_FAILURE. Tentamos como reforço.
    /// </summary>
    public static bool TryReadNvmeHealthViaPredictFailure(int driveIndex, out NvmeHealth health)
    {
        health = default;

        using var handle = OpenPhysicalDrive(driveIndex, requireWrite: false);
        if (handle is null) return false;

        var outBuf = new byte[4 + 512];
        bool ok = DeviceIoControl(handle, IOCTL_STORAGE_PREDICT_FAILURE, null, 0, outBuf, (uint)outBuf.Length,
            out _, IntPtr.Zero);
        if (!ok) return false;

        return TryParseNvmeHealthLog(outBuf, 4, out health);
    }

    /// <summary>Dados de saúde de um SSD NVMe, lidos da página de log SMART/Health (0x02).</summary>
    public struct NvmeHealth
    {
        public bool CriticalWarning;
        public double TemperatureCelsius;
        public int AvailableSparePercent;
        public int PercentageUsed;     // desgaste: vida restante ≈ 100 - este valor
        public double TerabytesWritten;
        public long PowerOnHours;
        public long MediaErrors;
    }

    /// <summary>
    /// Lê a página de log SMART/Health (0x02) de um SSD NVMe via IOCTL_STORAGE_QUERY_PROPERTY
    /// com StorageDeviceProtocolSpecificProperty — exatamente o mecanismo que ferramentas como
    /// o CrystalDiskInfo usam para NVMe, já que o protocolo NVMe não fala ATA SMART.
    /// </summary>
    public static bool TryReadNvmeHealth(int driveIndex, out NvmeHealth health)
    {
        health = default;

        using var handle = OpenPhysicalDrive(driveIndex, requireWrite: false);
        if (handle is null) return false;

        // STORAGE_PROPERTY_QUERY(8) + STORAGE_PROTOCOL_SPECIFIC_DATA(40) + log de 512 bytes.
        const int headerSize = 8;
        const int protoSize = 40;
        const int logSize = 512;
        var buf = new byte[headerSize + protoSize + logSize];

        WriteU32(buf, 0, 50);   // PropertyId = StorageDeviceProtocolSpecificProperty
        WriteU32(buf, 4, 0);    // QueryType = PropertyStandardQuery

        // STORAGE_PROTOCOL_SPECIFIC_DATA (começa no offset 8).
        WriteU32(buf, 8, 3);            // ProtocolType = ProtocolTypeNvme
        WriteU32(buf, 12, 2);           // DataType = NVMeDataTypeLogPage
        WriteU32(buf, 16, 0x02);        // ProtocolDataRequestValue = SMART/Health log page
        WriteU32(buf, 20, 0);           // ProtocolDataRequestSubValue
        WriteU32(buf, 24, protoSize);   // ProtocolDataOffset (relativo ao início desta struct)
        WriteU32(buf, 28, logSize);     // ProtocolDataLength

        bool ok = DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, buf, (uint)buf.Length,
            buf, (uint)buf.Length, out _, IntPtr.Zero);
        if (!ok) return false;

        // No retorno, o buffer é um STORAGE_PROTOCOL_DATA_DESCRIPTOR: a STORAGE_PROTOCOL_SPECIFIC_DATA
        // fica no offset 8 e seu campo ProtocolDataOffset (5º DWORD = offset 8+16=24) diz onde o
        // driver colocou o log. Usamos esse valor em vez de supor 48 — alguns drivers diferem.
        uint returnedOffset = ReadU32(buf, 8 + 16);
        int logOffset = returnedOffset is >= 40 and < 4096 ? headerSize + (int)returnedOffset : headerSize + protoSize;
        return TryParseNvmeHealthLog(buf, logOffset, out health);
    }

    // Layout da página de log "SMART/Health Information" (Log Page ID 0x02) do NVMe — igual em
    // qualquer fonte que devolva esses 512 bytes brutos (StorageDeviceProtocolSpecificProperty
    // ou o VendorSpecific de IOCTL_STORAGE_PREDICT_FAILURE).
    private static bool TryParseNvmeHealthLog(byte[] buf, int offset, out NvmeHealth health)
    {
        health = default;
        if (buf.Length < offset + 192) return false;

        byte criticalWarning = buf[offset + 0];
        ushort tempK = (ushort)(buf[offset + 1] | (buf[offset + 2] << 8));
        int availSpare = buf[offset + 3];
        int pctUsed = buf[offset + 5];
        ulong dataUnitsWritten = ReadU64(buf, offset + 48); // 64 bits baixos bastam
        ulong powerOnHours = ReadU64(buf, offset + 128);
        ulong mediaErrors = ReadU64(buf, offset + 160);

        // Sanidade: se tudo for zero, o controlador provavelmente não respondeu com dados reais.
        if (tempK == 0 && pctUsed == 0 && dataUnitsWritten == 0 && powerOnHours == 0 && availSpare == 0)
            return false;

        health = new NvmeHealth
        {
            CriticalWarning = criticalWarning != 0,
            TemperatureCelsius = tempK > 0 ? tempK - 273.15 : 0,
            AvailableSparePercent = availSpare,
            PercentageUsed = pctUsed,
            // 1 "data unit" = 1000 * 512 bytes (NVMe), então TBW = unidades * 512000 / 1e12.
            TerabytesWritten = dataUnitsWritten * 512000.0 / 1_000_000_000_000.0,
            PowerOnHours = (long)Math.Min(powerOnHours, long.MaxValue),
            MediaErrors = (long)Math.Min(mediaErrors, long.MaxValue),
        };
        return true;
    }

    private static ulong ReadU64(byte[] b, int o)
    {
        ulong v = 0;
        for (int i = 0; i < 8; i++) v |= (ulong)b[o + i] << (8 * i);
        return v;
    }

    private static uint ReadU32(byte[] b, int o)
    {
        uint v = 0;
        for (int i = 0; i < 4; i++) v |= (uint)b[o + i] << (8 * i);
        return v;
    }

    // Abre o disco físico. O nível de acesso depende do IOCTL:
    //
    // • ATA PASS THROUGH (SMART READ DATA/RETURN STATUS) EXIGE acesso de escrita. O disco de boot
    //   não pode ser aberto com escrita enquanto o Windows roda (a partição de sistema está montada),
    //   então, nesses casos, NÃO insistimos em níveis menores: um handle só-leitura faria o ATA pass
    //   through retornar lixo (em NVMe, via tradução SAT), poluindo o diagnóstico com erros falsos.
    //   Por isso, sem escrita, o ATA simplesmente fica indisponível (igual ao comportamento original).
    //
    // • Consultas de propriedade (NVMe health, falha prevista) funcionam com acesso reduzido e nem
    //   precisam de read/write — então aqui tentamos READ e, por fim, zero-access. É isso que permite
    //   ler o SMART de NVMe do próprio disco de boot e de vários discos secundários.
    private static SafeFileHandle? OpenPhysicalDrive(int index, bool requireWrite)
    {
        string path = $@"\\.\PhysicalDrive{index}";
        uint[] accessLevels = requireWrite
            ? new[] { GENERIC_READ | GENERIC_WRITE }
            : new[] { GENERIC_READ, 0u };

        foreach (uint access in accessLevels)
        {
            var handle = CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            handle.Dispose();
        }
        return null;
    }

    // Monta o buffer ATA_PASS_THROUGH_EX (cabeçalho de 48 bytes em x64) + 512 bytes de dados,
    // com o registrador "Features" definindo o subcomando SMART (0xD0=READ DATA, 0xDA=RETURN
    // STATUS) e a assinatura 0x4F/0xC2 que identifica comandos SMART no protocolo ATA8-ACS.
    private static byte[] BuildAtaPassThroughBuffer(byte feature, bool withDataPhase)
    {
        int total = withDataPhase ? AtaHeaderSize + 512 : AtaHeaderSize;
        var buf = new byte[total];

        WriteU16(buf, 0, (ushort)total);                                  // Length
        WriteU16(buf, 2, withDataPhase ? (ushort)0x06 : (ushort)0x04);    // AtaFlags (DATA_IN | DRDY_REQUIRED)
        WriteU32(buf, 8, withDataPhase ? 512u : 0u);                      // DataTransferLength
        WriteU32(buf, 12, 5);                                             // TimeOutValue (segundos)
        WriteU64(buf, 24, withDataPhase ? (ulong)AtaHeaderSize : 0ul);    // DataBufferOffset

        // CurrentTaskFile (offset 40..47): registradores do comando ATA.
        buf[40] = feature; // Features
        buf[41] = 0x01;    // SectorCount
        buf[42] = 0x00;    // LBA Low
        buf[43] = 0x4F;    // LBA Mid  — assinatura SMART
        buf[44] = 0xC2;    // LBA High — assinatura SMART
        buf[45] = 0xA0;    // Device
        buf[46] = 0xB0;    // Command — SMART
        buf[47] = 0x00;    // Reserved

        return buf;
    }

    private static void WriteU16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
    private static void WriteU32(byte[] b, int o, uint v) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); }
    private static void WriteU64(byte[] b, int o, ulong v) { for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i)); }
}
