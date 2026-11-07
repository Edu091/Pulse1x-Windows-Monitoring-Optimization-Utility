/*
 * Pulse1x Voice — plugin VST2 (64 bits) do modificador de voz do Pulse1x.
 *
 * O Equalizer APO carrega este plugin dentro do mecanismo de áudio do Windows, no próprio
 * microfone: o efeito vale em qualquer app (Discord, WhatsApp, Meet, jogos) sem microfone virtual.
 *
 * Cadeia de processamento, por canal:
 *   gate -> tom (phase vocoder + vibrato) -> STFT (robô / sussurro) -> anel -> distorção -> 8-bit -> eco
 *   -> ganho -> limitador
 *
 * Configuração: o Pulse1x passa um texto "chave=valor;..." em Base64 pelo ChunkData do APO.
 *   pitch=semitons  vib=semitons  vibrate=Hz  robot=Hz  whisper=0/1  ring=Hz  ringmix=0..1
 *   drive=1..20  crush=Hz  bits=2..16  echo=ms  echofb=0..0.9  echomix=0..1
 *   gain=dB  gate=dBFS (0 = desligado)
 *
 * A interface VST2 é declarada à mão (só a ABI binária, sem o SDK da Steinberg).
 * Compilar: ver build.ps1 (zig cc, alvo x86_64-windows-gnu).
 */
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <xmmintrin.h>
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define PI 3.14159265358979323846
#define MAXCH 2
#define MAXN 2048

/* ------------------------------------------------------------------ ABI VST2 */

typedef struct AEffect AEffect;
typedef intptr_t (*HostCallback)(AEffect *, int32_t, int32_t, intptr_t, void *, float);
typedef intptr_t (*DispatcherProc)(AEffect *, int32_t, int32_t, intptr_t, void *, float);
typedef void (*ProcessProc)(AEffect *, float **, float **, int32_t);
typedef void (*SetParameterProc)(AEffect *, int32_t, float);
typedef float (*GetParameterProc)(AEffect *, int32_t);

struct AEffect {
    int32_t magic;
    DispatcherProc dispatcher;
    ProcessProc process;
    SetParameterProc setParameter;
    GetParameterProc getParameter;
    int32_t numPrograms, numParams, numInputs, numOutputs, flags;
    intptr_t resvd1, resvd2;
    int32_t initialDelay, realQualities, offQualities;
    float ioRatio;
    void *object, *user;
    int32_t uniqueID, version;
    ProcessProc processReplacing;
    void *processDoubleReplacing;
    char future[56];
};

enum {
    effOpen = 0, effClose = 1, effSetSampleRate = 10, effSetBlockSize = 11, effMainsChanged = 12,
    effGetChunk = 23, effSetChunk = 24, effGetEffectName = 45, effGetVendorString = 47,
    effGetProductString = 48, effGetVendorVersion = 49, effCanDo = 51, effGetVstVersion = 58,
};
#define FLAG_CAN_REPLACING (1 << 4)
#define FLAG_PROGRAM_CHUNKS (1 << 5)

/* ------------------------------------------------------------------ Parâmetros */

typedef struct {
    float pitch, vib, vibRate, robotHz, ringHz, ringMix, drive, crushHz, crushBits;
    float echoMs, echoFb, echoMix, gainDb, gateDb;
    int whisper;
    int sb; /* mesa de sons: só a instância do microfone lê a memória compartilhada */
} Params;

static void params_default(Params *p) {
    memset(p, 0, sizeof(*p));
    p->vibRate = 5.0f;
    p->ringMix = 1.0f;
    p->drive = 1.0f;
    p->crushBits = 16.0f;
    p->echoFb = 0.4f;
    p->echoMix = 0.5f;
}

static float clampf(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }

static void params_parse(Params *p, const char *text, size_t len) {
    params_default(p);
    char key[32], val[32];
    size_t i = 0;
    while (i < len) {
        size_t k = 0, v = 0;
        while (i < len && text[i] != '=' && text[i] != ';' && k < sizeof(key) - 1) key[k++] = text[i++];
        key[k] = 0;
        if (i < len && text[i] == '=') i++;
        while (i < len && text[i] != ';' && v < sizeof(val) - 1) val[v++] = text[i++];
        val[v] = 0;
        while (i < len && text[i] != ';') i++;
        if (i < len) i++;
        float f = (float)atof(val);
        if (!strcmp(key, "pitch")) p->pitch = clampf(f, -24, 24);
        else if (!strcmp(key, "vib")) p->vib = clampf(f, 0, 12);
        else if (!strcmp(key, "vibrate")) p->vibRate = clampf(f, 0.05f, 20);
        else if (!strcmp(key, "robot")) p->robotHz = clampf(f, 0, 400);
        else if (!strcmp(key, "whisper")) p->whisper = f > 0.5f;
        else if (!strcmp(key, "ring")) p->ringHz = clampf(f, 0, 4000);
        else if (!strcmp(key, "ringmix")) p->ringMix = clampf(f, 0, 1);
        else if (!strcmp(key, "drive")) p->drive = clampf(f, 1, 40);
        else if (!strcmp(key, "crush")) p->crushHz = clampf(f, 0, 48000);
        else if (!strcmp(key, "bits")) p->crushBits = clampf(f, 2, 16);
        else if (!strcmp(key, "echo")) p->echoMs = clampf(f, 0, 1000);
        else if (!strcmp(key, "echofb")) p->echoFb = clampf(f, 0, 0.9f);
        else if (!strcmp(key, "echomix")) p->echoMix = clampf(f, 0, 1);
        else if (!strcmp(key, "gain")) p->gainDb = clampf(f, -24, 30);
        else if (!strcmp(key, "gate")) p->gateDb = clampf(f, -90, 0);
        else if (!strcmp(key, "sb")) p->sb = f > 0.5f;
    }
}

/* ------------------------------------------------------------------ FFT */

static void fft(float *re, float *im, int n, int inverse) {
    for (int i = 1, j = 0; i < n; i++) {
        int bit = n >> 1;
        for (; j & bit; bit >>= 1) j ^= bit;
        j ^= bit;
        if (i < j) {
            float t = re[i]; re[i] = re[j]; re[j] = t;
            t = im[i]; im[i] = im[j]; im[j] = t;
        }
    }
    for (int len = 2; len <= n; len <<= 1) {
        double ang = 2 * PI / len * (inverse ? 1 : -1);
        float wr = (float)cos(ang), wi = (float)sin(ang);
        for (int i = 0; i < n; i += len) {
            float cr = 1, ci = 0;
            for (int j = 0; j < len / 2; j++) {
                int a = i + j, b = i + j + len / 2;
                float xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                re[b] = re[a] - xr; im[b] = im[a] - xi;
                re[a] += xr; im[a] += xi;
                float nr = cr * wr - ci * wi;
                ci = cr * wi + ci * wr;
                cr = nr;
            }
        }
    }
}

/* ------------------------------------------------------------------ Estado por canal */

typedef struct {
    /* tom (phase vocoder) */
    float pin[MAXN], pout[MAXN], pacc[2 * MAXN], pre[MAXN], pim[MAXN];
    float lastPhase[MAXN / 2 + 1], sumPhase[MAXN / 2 + 1];
    float anaMag[MAXN / 2 + 1], anaFreq[MAXN / 2 + 1], synMag[MAXN / 2 + 1], synFreq[MAXN / 2 + 1];
    int rover;
    double vibPhase;
    /* STFT */
    float in[MAXN], acc[MAXN], re[MAXN], im[MAXN];
    int inPos, accPos, sinceHop;
    /* efeitos simples */
    double ringPhase, crushAcc;
    float crushHold;
    float *ebuf;
    int ebufLen, ew;
    /* dinâmica */
    float gateEnv, gateGain, limEnv;
    uint32_t rng;
} Channel;

typedef struct {
    AEffect fx;
    Params p;
    float sr;
    int N, hop, pN;
    float win[MAXN], pwin[MAXN];
    float stftNorm;
    Channel *ch[MAXCH];
    char chunk[512];
    /* mesa de sons */
    HANDLE sbMap;
    struct SbShared *sbView;
    int sbRetry;
    LONG sbFlushSeen;
    float sbBuf[4096];
    int chunkLen;
} Plugin;

static uint32_t xorshift(uint32_t *s) {
    uint32_t x = *s;
    x ^= x << 13; x ^= x >> 17; x ^= x << 5;
    return *s = x;
}

static void plugin_reset(Plugin *pl) {
    float sr = pl->sr;
    /* Janela da STFT: ~21 ms (1024 a 48 kHz, 512 a 24 kHz, 256 a 16 kHz é curto demais -> 512). */
    int N = 256;
    while (N < MAXN && N < sr * 0.021f) N <<= 1;
    if (N < 512) N = 512;
    pl->N = N;
    int hop = N / 4;
    if (pl->p.robotHz > 0) {
        /* Robô: o salto entre quadros vira a frequência do zumbido. */
        hop = (int)(sr / pl->p.robotHz + 0.5f);
        if (hop < 16) hop = 16;
        if (hop > N / 2) hop = N / 2;
    }
    pl->hop = hop;
    double wsum = 0;
    for (int i = 0; i < N; i++) {
        pl->win[i] = (float)(0.5 - 0.5 * cos(2 * PI * i / N));
        wsum += pl->win[i] * pl->win[i];
    }
    pl->stftNorm = (float)(hop / wsum);

    /* Janela do phase vocoder: ~43 ms (2048 a 48 kHz, 1024 a 24 kHz, 512 a 16 kHz). */
    int pN = 256;
    while (pN < MAXN && pN < sr * 0.040f) pN <<= 1;
    pl->pN = pN;
    for (int i = 0; i < pN; i++) pl->pwin[i] = (float)(0.5 - 0.5 * cos(2 * PI * i / pN));

    int elen = (int)(sr * 1.05f) + 2;
    for (int c = 0; c < MAXCH; c++) {
        Channel *ch = pl->ch[c];
        float *ebuf = ch->ebuf;
        int oldLen = ch->ebufLen;
        memset(ch, 0, sizeof(*ch));
        if (ebuf && oldLen >= elen) { ch->ebuf = ebuf; ch->ebufLen = oldLen; }
        else { free(ebuf); ch->ebuf = (float *)calloc((size_t)elen, sizeof(float)); ch->ebufLen = ch->ebuf ? elen : 0; }
        if (ch->ebuf) memset(ch->ebuf, 0, sizeof(float) * (size_t)ch->ebufLen);
        ch->gateGain = 1;
        ch->rng = 0x9E3779B9u; /* mesma semente: canais iguais saem iguais */
    }
}

/* ------------------------------------------------------------------ Blocos de efeito */

/*
 * Mudança de tom por phase vocoder: para cada quadro (janela de ~43 ms, 4x sobreposição) estima a
 * frequência real de cada faixa pela diferença de fase entre quadros, multiplica as frequências
 * pela razão do tom e ressintetiza acumulando a fase. Mantém a duração e soa muito mais limpo que
 * a troca por linha de atraso, sem o "tremido" a cada janela.
 */
#define OSAMP 4
static float pitch_shift(Plugin *pl, Channel *ch, float x) {
    Params *p = &pl->p;
    if (p->pitch == 0.0f && p->vib <= 0) return x;

    int N = pl->pN, step = N / OSAMP, latency = N - step;
    if (ch->rover < latency) ch->rover = latency;
    ch->pin[ch->rover] = x;
    float y = ch->pout[ch->rover - latency];
    ch->rover++;
    if (ch->rover < N) return y;
    ch->rover = latency;

    float semis = p->pitch;
    if (p->vib > 0) {
        semis += p->vib * (float)sin(2 * PI * ch->vibPhase);
        ch->vibPhase += p->vibRate * step / pl->sr;
        if (ch->vibPhase >= 1) ch->vibPhase -= 1;
    }
    float shift = powf(2, semis / 12);
    double freqPerBin = pl->sr / N, expct = 2 * PI * step / N;
    int half = N / 2;

    for (int k = 0; k < N; k++) { ch->pre[k] = ch->pin[k] * pl->pwin[k]; ch->pim[k] = 0; }
    fft(ch->pre, ch->pim, N, 0);

    /* Análise: magnitude, fase e frequência real de cada faixa. */
    float *mag = ch->anaMag, *freq = ch->anaFreq, *aph = ch->synMag, *newPh = ch->synFreq;
    for (int k = 0; k <= half; k++) {
        double re = ch->pre[k], im = ch->pim[k];
        double phase = atan2(im, re);
        double d = phase - ch->lastPhase[k];
        ch->lastPhase[k] = (float)phase;
        d -= k * expct;
        long q = (long)(d / PI);
        if (q >= 0) q += q & 1; else q -= q & 1;
        d -= PI * q;
        d = OSAMP * d / (2 * PI);
        mag[k] = (float)(2 * sqrt(re * re + im * im));
        aph[k] = (float)phase;
        freq[k] = (float)((k + d) * freqPerBin);
        newPh[k] = ch->sumPhase[k];
    }

    /* Picos do espectro (máximos locais em +-2 faixas). */
    int peaks[MAXN / 4], npk = 0;
    for (int k = 2; k <= half - 2 && npk < MAXN / 4; k++) {
        float m = mag[k];
        if (m > 1e-7f && m > mag[k - 1] && m >= mag[k + 1] && m > mag[k - 2] && m >= mag[k + 2]) peaks[npk++] = k;
    }

    /* Síntese: cada pico vai, como um bloco, para a faixa (pico x razão). A fase do pico avança
       pela frequência nova; as faixas ao redor mantêm a fase relativa ao pico (phase locking). */
    for (int k = 0; k < N; k++) { ch->pre[k] = 0; ch->pim[k] = 0; }
    for (int i = 0; i < npk; i++) {
        int p = peaks[i];
        int lo = i == 0 ? 0 : (peaks[i - 1] + p) / 2 + 1;
        int hi = i == npk - 1 ? half : (p + peaks[i + 1]) / 2;
        int q = (int)(p * shift + 0.5f);
        if (q > half) break;
        double phq = ch->sumPhase[q] + 2 * PI * freq[p] * shift * step / pl->sr;
        phq = fmod(phq, 2 * PI);
        for (int k = lo; k <= hi; k++) {
            int t = q + (k - p);
            if (t < 0 || t > half) continue;
            double ph = phq + (aph[k] - aph[p]);
            ch->pre[t] += mag[k] * (float)cos(ph);
            ch->pim[t] += mag[k] * (float)sin(ph);
            newPh[t] = (float)ph;
        }
    }
    memcpy(ch->sumPhase, newPh, sizeof(float) * (size_t)(half + 1));
    for (int k = half + 1; k < N; k++) { ch->pre[k] = 0; ch->pim[k] = 0; }    fft(ch->pre, ch->pim, N, 1);
    /* 2/(N/2): espectro de um lado só; 1,5 = soma de hann^2 com 4x de sobreposição. */
    float norm = 2.0f / (half * OSAMP * 0.375f * OSAMP);
    for (int k = 0; k < N; k++) ch->pacc[k] += pl->pwin[k] * ch->pre[k] * norm;
    for (int k = 0; k < step; k++) ch->pout[k] = ch->pacc[k];
    memmove(ch->pacc, ch->pacc + step, sizeof(float) * (size_t)N);
    memset(ch->pacc + N, 0, sizeof(float) * (size_t)step);
    memmove(ch->pin, ch->pin + step, sizeof(float) * (size_t)latency);
    return y;
}
/* Robô (fase zerada) e sussurro (fase aleatória) por STFT com sobreposição-soma. */
static float stft_process(Plugin *pl, Channel *ch, float x) {
    int N = pl->N;
    ch->in[ch->inPos] = x;
    ch->inPos = (ch->inPos + 1) % N;
    float y = ch->acc[ch->accPos];
    ch->acc[ch->accPos] = 0;
    ch->accPos = (ch->accPos + 1) % N;

    if (++ch->sinceHop >= pl->hop) {
        ch->sinceHop = 0;
        for (int i = 0; i < N; i++) {
            ch->re[i] = ch->in[(ch->inPos + i) % N] * pl->win[i];
            ch->im[i] = 0;
        }
        fft(ch->re, ch->im, N, 0);
        for (int k = 0; k <= N / 2; k++) {
            float mag = sqrtf(ch->re[k] * ch->re[k] + ch->im[k] * ch->im[k]);
            if (pl->p.robotHz > 0) {
                ch->re[k] = mag; ch->im[k] = 0;
            } else {
                float ph = (float)(xorshift(&ch->rng) * (2.0 * PI / 4294967296.0));
                ch->re[k] = mag * cosf(ph); ch->im[k] = mag * sinf(ph);
            }
            if (k > 0 && k < N / 2) { ch->re[N - k] = ch->re[k]; ch->im[N - k] = -ch->im[k]; }
        }
        ch->im[0] = 0; ch->im[N / 2] = 0;
        fft(ch->re, ch->im, N, 1);
        float scale = pl->stftNorm / N;
        /* Robô: desloca o quadro para centralizar o pulso (fase zero = pico no início). */
        int shift = pl->p.robotHz > 0 ? N / 2 : 0;
        for (int i = 0; i < N; i++) {
            float s = ch->re[(i + shift) % N] * pl->win[i] * scale;
            ch->acc[(ch->accPos + i) % N] += s;
        }
    }
    return y;
}

static float process_sample(Plugin *pl, Channel *ch, float x, float gain, float gateThr,
                            float gateAtt, float gateRel, float limRel, float extra) {
    Params *p = &pl->p;

    /* Portão de ruído antes de tudo: com muito ganho, o chiado do ambiente apareceria. */
    if (gateThr > 0) {
        float a = fabsf(x);
        ch->gateEnv = a > ch->gateEnv ? a : ch->gateEnv * 0.9995f;
        float target = ch->gateEnv > gateThr ? 1.0f : 0.0f;
        float k = target > ch->gateGain ? gateAtt : gateRel;
        ch->gateGain += (target - ch->gateGain) * k;
        x *= ch->gateGain;
    }

    x = pitch_shift(pl, ch, x);
    if (p->robotHz > 0 || p->whisper) x = stft_process(pl, ch, x);

    if (p->ringHz > 0) {
        float m = (float)sin(2 * PI * ch->ringPhase);
        ch->ringPhase += p->ringHz / pl->sr;
        if (ch->ringPhase >= 1) ch->ringPhase -= 1;
        x = x * (1 - p->ringMix) + x * m * p->ringMix;
    }

    if (p->drive > 1.01f) x = tanhf(x * p->drive) / tanhf(p->drive);

    if (p->crushHz > 0 && p->crushHz < pl->sr) {
        ch->crushAcc += p->crushHz / pl->sr;
        if (ch->crushAcc >= 1) {
            ch->crushAcc -= 1;
            float q = powf(2, p->crushBits - 1);
            ch->crushHold = roundf(x * q) / q;
        }
        x = ch->crushHold;
    }

    if (p->echoMs > 0 && ch->ebuf) {
        int d = (int)(p->echoMs * pl->sr / 1000);
        if (d >= ch->ebufLen) d = ch->ebufLen - 1;
        if (d < 1) d = 1;
        int r = ch->ew - d;
        if (r < 0) r += ch->ebufLen;
        float e = ch->ebuf[r];
        float fbk = x + e * p->echoFb;
        if (fabsf(fbk) < 1e-15f) fbk = 0;
        ch->ebuf[ch->ew] = fbk;
        ch->ew = (ch->ew + 1) % ch->ebufLen;
        x = x + e * p->echoMix;
    }

    x *= gain;
    x += extra; /* mesa de sons: fora dos efeitos e do ganho da voz */

    /* Limitador de pico em -1 dBFS: permite muito ganho sem o estalo do clipping digital. */
    const float ceiling = 0.89f;
    float a = fabsf(x);
    ch->limEnv = a > ch->limEnv ? a : ch->limEnv * limRel + a * (1 - limRel);
    if (ch->limEnv > ceiling) x *= ceiling / ch->limEnv;
    if (x > 0.98f) x = 0.98f;
    if (x < -0.98f) x = -0.98f;
    if (!isfinite(x)) x = 0;
    return x;
}

/* ------------------------------------------------------------------ Mesa de sons
 *
 * O Pulse1x (elevado) cria "Global\Pulse1xSoundboard": um cabeçalho e um anel de amostras float
 * na taxa do microfone. O plugin, dentro do serviço de áudio, mistura essas amostras à voz depois
 * dos efeitos e antes do limitador — é assim que os sons chegam à call em qualquer app.
 * Layout (fixo, espelhado em SoundboardChannel.cs):
 *   0 magic | 4 version | 8 capacity | 12 sampleRate | 16 writePos | 24 readPos | 32 flushSeq
 *   36 volume | 40 consumerRate | 44..79 reservado | 80 dados
 */
#define SB_NAME L"Global\\Pulse1xSoundboard"
#define SB_MAGIC 0x31425350u /* 'PSB1' */

typedef struct SbShared {
    uint32_t magic, version, capacity, sampleRate;
    volatile LONG64 writePos;
    volatile LONG64 readPos;
    volatile LONG flushSeq;
    float volume;
    volatile LONG consumerRate;
    uint32_t reserved[9];
    float data[1];
} SbShared;

static void sb_close(Plugin *pl) {
    if (pl->sbView) UnmapViewOfFile(pl->sbView);
    if (pl->sbMap) CloseHandle(pl->sbMap);
    pl->sbView = NULL;
    pl->sbMap = NULL;
}

static void sb_open(Plugin *pl) {
    HANDLE map = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, SB_NAME);
    if (!map) return;
    SbShared *view = (SbShared *)MapViewOfFile(map, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, 0);
    if (!view || view->magic != SB_MAGIC || view->capacity == 0) {
        if (view) UnmapViewOfFile(view);
        CloseHandle(map);
        return;
    }
    pl->sbMap = map;
    pl->sbView = view;
    pl->sbFlushSeen = view->flushSeq;
}

/* Copia até n amostras pendentes da mesa de sons para dst; devolve quantas copiou. */
static int sb_pull(Plugin *pl, float *dst, int n) {
    if (!pl->p.sb) return 0;
    if (!pl->sbView) {
        pl->sbRetry -= n;
        if (pl->sbRetry > 0) return 0;
        pl->sbRetry = (int)(pl->sr / 2); /* tenta abrir de novo a cada ~0,5 s */
        sb_open(pl);
        if (!pl->sbView) return 0;
    }
    SbShared *s = pl->sbView;
    if (s->magic != SB_MAGIC) return 0;
    s->consumerRate = (LONG)pl->sr;

    LONG flush = s->flushSeq;
    LONG64 w = s->writePos, r = s->readPos;
    if (flush != pl->sbFlushSeen) { pl->sbFlushSeen = flush; s->readPos = w; return 0; }
    LONG64 cap = s->capacity;
    if (w - r > cap) r = w - cap;
    LONG64 avail = w - r;
    if (avail <= 0) return 0;
    if (avail > n) avail = n;
    if ((uint32_t)(pl->sr + 0.5f) != s->sampleRate) { s->readPos = w; return 0; } /* taxa errada: descarta */
    MemoryBarrier();
    float vol = s->volume;
    for (LONG64 i = 0; i < avail; i++) dst[i] = s->data[(r + i) % cap] * vol;
    s->readPos = r + avail;
    return (int)avail;
}
/* ------------------------------------------------------------------ Callbacks VST */

static void process_replacing(AEffect *fx, float **inputs, float **outputs, int32_t frames) {
    Plugin *pl = (Plugin *)fx->object;
    unsigned int csr = _mm_getcsr();
    _mm_setcsr(csr | 0x8040); /* sem denormais (flush-to-zero / denormals-are-zero) */

    float gain = powf(10, pl->p.gainDb / 20);
    float gateThr = pl->p.gateDb < 0 ? powf(10, pl->p.gateDb / 20) : 0;
    float gateAtt = 1 - expf(-1.0f / (0.002f * pl->sr));
    float gateRel = 1 - expf(-1.0f / (0.15f * pl->sr));
    float limRel = expf(-1.0f / (0.08f * pl->sr));

    /* Em blocos: o som da mesa é lido uma vez por bloco e somado a todos os canais. */
    for (int start = 0; start < frames; start += 4096) {
        int n = frames - start < 4096 ? frames - start : 4096;
        int got = sb_pull(pl, pl->sbBuf, n);
        for (int c = 0; c < fx->numOutputs && c < MAXCH; c++) {
            float *in = inputs[c], *out = outputs[c];
            if (!in || !out) continue;
            Channel *ch = pl->ch[c];
            for (int i = 0; i < n; i++)
                out[start + i] = process_sample(pl, ch, in[start + i], gain, gateThr, gateAtt, gateRel, limRel,
                                                i < got ? pl->sbBuf[i] : 0.0f);
        }
    }
    _mm_setcsr(csr);
}

static void process_accumulating(AEffect *fx, float **inputs, float **outputs, int32_t frames) {
    (void)fx; (void)inputs; (void)outputs; (void)frames;
}

static void set_parameter(AEffect *fx, int32_t index, float value) { (void)fx; (void)index; (void)value; }
static float get_parameter(AEffect *fx, int32_t index) { (void)fx; (void)index; return 0; }

static void copy_str(void *dst, const char *src, size_t max) {
    if (!dst) return;
    strncpy((char *)dst, src, max - 1);
    ((char *)dst)[max - 1] = 0;
}

static void plugin_free(Plugin *pl) {
    sb_close(pl);
    for (int c = 0; c < MAXCH; c++) {
        if (pl->ch[c]) { free(pl->ch[c]->ebuf); free(pl->ch[c]); }
    }
    free(pl);
}

static intptr_t dispatcher(AEffect *fx, int32_t op, int32_t index, intptr_t value, void *ptr, float opt) {
    Plugin *pl = (Plugin *)fx->object;
    (void)index;
    switch (op) {
    case effOpen: return 0;
    case effClose: plugin_free(pl); return 1;
    case effSetSampleRate:
        if (opt > 1000 && opt < 400000) { pl->sr = opt; plugin_reset(pl); }
        return 1;
    case effSetBlockSize: return 1;
    case effMainsChanged: if (value) plugin_reset(pl); return 1;
    case effGetChunk:
        if (ptr) *(void **)ptr = pl->chunk;
        return pl->chunkLen;
    case effSetChunk: {
        int len = (int)value;
        if (len < 0) len = 0;
        if (len > (int)sizeof(pl->chunk) - 1) len = (int)sizeof(pl->chunk) - 1;
        if (ptr && len) memcpy(pl->chunk, ptr, (size_t)len);
        pl->chunk[len] = 0;
        pl->chunkLen = len;
        params_parse(&pl->p, pl->chunk, (size_t)len);
        plugin_reset(pl);
        return 1;
    }
    case effGetEffectName: copy_str(ptr, "Pulse1x Voice", 32); return 1;
    case effGetVendorString: copy_str(ptr, "Pulse1x", 64); return 1;
    case effGetProductString: copy_str(ptr, "Pulse1x Voice", 64); return 1;
    case effGetVendorVersion: return 1000;
    case effCanDo: return 0;
    case effGetVstVersion: return 2400;
    default: return 0;
    }
}

__declspec(dllexport) AEffect *VSTPluginMain(HostCallback host) {
    (void)host;
    Plugin *pl = (Plugin *)calloc(1, sizeof(Plugin));
    if (!pl) return NULL;
    for (int c = 0; c < MAXCH; c++) {
        pl->ch[c] = (Channel *)calloc(1, sizeof(Channel));
        if (!pl->ch[c]) { plugin_free(pl); return NULL; }
    }
    params_default(&pl->p);
    pl->sr = 48000;
    plugin_reset(pl);

    AEffect *fx = &pl->fx;
    fx->magic = 0x56737450; /* 'VstP' */
    fx->dispatcher = dispatcher;
    fx->process = process_accumulating;
    fx->setParameter = set_parameter;
    fx->getParameter = get_parameter;
    fx->numPrograms = 1;
    fx->numParams = 0;
    fx->numInputs = MAXCH;
    fx->numOutputs = MAXCH;
    fx->flags = FLAG_CAN_REPLACING | FLAG_PROGRAM_CHUNKS;
    fx->ioRatio = 1;
    fx->object = pl;
    fx->uniqueID = 0x50317856; /* 'P1xV' */
    fx->version = 1000;
    fx->processReplacing = process_replacing;
    return fx;
}
