using System.Collections.Generic;
using Pulse1x.App.Localization;

namespace Pulse1x.App.Models;

/// <summary>Classificação textual derivada de uma nota de 0 a 100.</summary>
public enum HealthRating { Excelente, MuitoBom, Bom, Atencao, Critico }

/// <summary>Severidade de um problema detectado no diagnóstico.</summary>
public enum ProblemSeverity { Baixa, Moderada, Alta, Critica }

/// <summary>Nível qualitativo de um risco futuro.</summary>
public enum RiskLevel { MuitoBaixo, Baixo, Moderado, Alto, MuitoAlto }

/// <summary>Um par rótulo/valor exibido dentro do cartão de um componente.</summary>
public record HealthMetric(string Label, string Value);

/// <summary>Uma linha de atributo SMART, no estilo CrystalDiskInfo (nome + valor + saudável?).</summary>
public class SmartAttributeRow
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public bool IsHealthy { get; init; } = true;
    public string StatusIcon => IsHealthy ? "✓" : "⚠";
    public string StatusColor => IsHealthy ? "#16A34A" : "#DC2626";
}

/// <summary>Resultado da avaliação de um componente (CPU, GPU, disco, etc.).</summary>
public class ComponentHealth
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Icon { get; init; } = "•";
    public int Score { get; set; }
    public string Summary { get; set; } = "";
    public List<HealthMetric> Metrics { get; } = new();

    /// <summary>Tabela de atributos SMART (estilo CrystalDiskInfo) — só preenchida para discos.</summary>
    public List<SmartAttributeRow> SmartAttributes { get; } = new();
    public bool HasSmartAttributes => SmartAttributes.Count > 0;

    public HealthRating Rating => HealthScale.RatingFromScore(Score);
    public string ScoreText => $"{Score}/100";
    public string RatingLabel => HealthScale.RatingLabel(Rating);
    public string RatingColor => HealthScale.RatingColor(Rating);
}

/// <summary>Problema identificado, com severidade.</summary>
public class HealthProblem
{
    public string Description { get; init; } = "";
    public ProblemSeverity Severity { get; init; }

    public string SeverityLabel => HealthScale.SeverityLabel(Severity);
    public string SeverityColor => HealthScale.SeverityColor(Severity);
    public string Display => $"⚠  {Description}";
}

/// <summary>Recomendação em linguagem simples.</summary>
public class Recommendation
{
    public string Text { get; init; } = "";
    public string Display => $"✓  {Text}";
}

/// <summary>Risco futuro estimado.</summary>
public class RiskItem
{
    public string Name { get; init; } = "";
    public RiskLevel Level { get; init; }

    public string LevelLabel => HealthScale.RiskLabel(Level);
    public string LevelColor => HealthScale.RiskColor(Level);
}

/// <summary>Relatório completo do Diagnóstico Inteligente.</summary>
public class HealthReport
{
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public int OverallScore { get; set; }

    public List<ComponentHealth> Components { get; } = new();
    public List<HealthProblem> Problems { get; } = new();
    public List<Recommendation> Recommendations { get; } = new();
    public List<RiskItem> Risks { get; } = new();

    public HealthRating OverallRating => HealthScale.RatingFromScore(OverallScore);
    public string OverallScoreText => $"{OverallScore}/100";
    public string OverallRatingLabel => HealthScale.RatingLabel(OverallRating);
    public string OverallColor => HealthScale.RatingColor(OverallRating);
    public string GeneratedAtText => GeneratedAt.ToString("dd/MM/yyyy HH:mm:ss");

    public bool HasProblems => Problems.Count > 0;
    public bool HasNoProblems => Problems.Count == 0;
}

/// <summary>Tabelas de conversão nota → classificação/cor e severidade/risco → rótulo/cor.</summary>
public static class HealthScale
{
    public static HealthRating RatingFromScore(int score) => score switch
    {
        >= 95 => HealthRating.Excelente,
        >= 85 => HealthRating.MuitoBom,
        >= 70 => HealthRating.Bom,
        >= 50 => HealthRating.Atencao,
        _ => HealthRating.Critico,
    };

    public static string RatingLabel(HealthRating r) => r switch
    {
        HealthRating.Excelente => Loc.S("Health_RatingExcellent"),
        HealthRating.MuitoBom => Loc.S("Health_RatingVeryGood"),
        HealthRating.Bom => Loc.S("Health_RatingGood"),
        HealthRating.Atencao => Loc.S("Health_RatingWarning"),
        _ => Loc.S("Health_RatingCritical"),
    };

    // Verde (ótimo) → vermelho de marca (crítico).
    public static string RatingColor(HealthRating r) => r switch
    {
        HealthRating.Excelente => "#16A34A",
        HealthRating.MuitoBom => "#22C55E",
        HealthRating.Bom => "#CA8A04",
        HealthRating.Atencao => "#EA580C",
        _ => "#DC2626",
    };

    public static string SeverityLabel(ProblemSeverity s) => s switch
    {
        ProblemSeverity.Baixa => Loc.S("Health_SeverityLow"),
        ProblemSeverity.Moderada => Loc.S("Health_SeverityModerate"),
        ProblemSeverity.Alta => Loc.S("Health_SeverityHigh"),
        _ => Loc.S("Health_SeverityCritical"),
    };

    public static string SeverityColor(ProblemSeverity s) => s switch
    {
        ProblemSeverity.Baixa => "#CA8A04",
        ProblemSeverity.Moderada => "#EA580C",
        ProblemSeverity.Alta => "#DC2626",
        _ => "#991B1B",
    };

    public static string RiskLabel(RiskLevel l) => l switch
    {
        RiskLevel.MuitoBaixo => Loc.S("Health_RiskVeryLow"),
        RiskLevel.Baixo => Loc.S("Health_RiskLow"),
        RiskLevel.Moderado => Loc.S("Health_RiskModerate"),
        RiskLevel.Alto => Loc.S("Health_RiskHigh"),
        _ => Loc.S("Health_RiskVeryHigh"),
    };

    public static string RiskColor(RiskLevel l) => l switch
    {
        RiskLevel.MuitoBaixo => "#16A34A",
        RiskLevel.Baixo => "#22C55E",
        RiskLevel.Moderado => "#CA8A04",
        RiskLevel.Alto => "#EA580C",
        _ => "#DC2626",
    };
}
