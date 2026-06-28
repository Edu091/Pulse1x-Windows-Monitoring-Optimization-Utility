namespace Pulse1x.App.Models;

/// <summary>Um par rótulo/valor exibido na página de detalhes (ex.: "Núcleos" → "8").</summary>
public record DetailItem(string Label, string Value);

/// <summary>Um grupo de itens sob um subtítulo (ex.: "Módulo 1 (DIMM A1)").</summary>
public record DetailGroup(string Name, IReadOnlyList<DetailItem> Items);

/// <summary>Conjunto completo de informações detalhadas de um componente de hardware.</summary>
public record ComponentDetails(string Title, string Subtitle, IReadOnlyList<DetailGroup> Groups);
