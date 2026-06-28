using System.Text;
using Pulse1x.App.Models;

namespace Pulse1x.App.Services;

/// <summary>Serializa um <see cref="HealthReport"/> em texto, JSON ou conteúdo imprimível (PDF).</summary>
public static class HealthReportExporter
{
    public static string BuildText(HealthReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DIAGNÓSTICO INTELIGENTE — Pulse1x ===");
        sb.AppendLine($"Gerado em: {r.GeneratedAtText}");
        sb.AppendLine();
        sb.AppendLine($"SAÚDE GERAL DO PC: {r.OverallScoreText}  ({r.OverallRatingLabel})");
        sb.AppendLine();

        sb.AppendLine("[ NOTAS POR COMPONENTE ]");
        foreach (var comp in r.Components)
            sb.AppendLine($"  {comp.Name,-22} {comp.ScoreText,8}  ({comp.RatingLabel})");
        sb.AppendLine();

        foreach (var comp in r.Components)
        {
            sb.AppendLine($"[ {comp.Name.ToUpperInvariant()} — {comp.ScoreText} ]");
            foreach (var m in comp.Metrics)
                sb.AppendLine($"  - {m.Label}: {m.Value}");
            sb.AppendLine($"  Estado: {comp.Summary}");
            sb.AppendLine();
        }

        sb.AppendLine("[ PROBLEMAS DETECTADOS ]");
        if (r.Problems.Count == 0) sb.AppendLine("  Nenhum problema relevante encontrado.");
        else foreach (var p in r.Problems)
            sb.AppendLine($"  ⚠ [{p.SeverityLabel}] {p.Description}");
        sb.AppendLine();

        sb.AppendLine("[ RECOMENDAÇÕES ]");
        foreach (var rec in r.Recommendations)
            sb.AppendLine($"  ✓ {rec.Text}");
        sb.AppendLine();

        sb.AppendLine("[ RISCOS ]");
        foreach (var risk in r.Risks)
            sb.AppendLine($"  {risk.Name}: {risk.LevelLabel}");
        sb.AppendLine();

        return sb.ToString();
    }

    public static string BuildJson(HealthReport r)
    {
        // JSON manual (sem dependências): suficiente para um relatório estruturado.
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"geradoEm\": \"{r.GeneratedAt:o}\",");
        sb.AppendLine($"  \"saudeGeral\": {r.OverallScore},");
        sb.AppendLine($"  \"classificacao\": \"{Esc(r.OverallRatingLabel)}\",");

        sb.AppendLine("  \"componentes\": [");
        for (int i = 0; i < r.Components.Count; i++)
        {
            var c = r.Components[i];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"nome\": \"{Esc(c.Name)}\",");
            sb.AppendLine($"      \"nota\": {c.Score},");
            sb.AppendLine($"      \"classificacao\": \"{Esc(c.RatingLabel)}\",");
            sb.AppendLine($"      \"estado\": \"{Esc(c.Summary)}\",");
            sb.AppendLine("      \"metricas\": {");
            for (int j = 0; j < c.Metrics.Count; j++)
            {
                var m = c.Metrics[j];
                sb.AppendLine($"        \"{Esc(m.Label)}\": \"{Esc(m.Value)}\"{(j < c.Metrics.Count - 1 ? "," : "")}");
            }
            sb.AppendLine("      }");
            sb.AppendLine($"    }}{(i < r.Components.Count - 1 ? "," : "")}");
        }
        sb.AppendLine("  ],");

        sb.AppendLine("  \"problemas\": [");
        for (int i = 0; i < r.Problems.Count; i++)
        {
            var p = r.Problems[i];
            sb.AppendLine($"    {{ \"severidade\": \"{Esc(p.SeverityLabel)}\", \"descricao\": \"{Esc(p.Description)}\" }}{(i < r.Problems.Count - 1 ? "," : "")}");
        }
        sb.AppendLine("  ],");

        sb.AppendLine("  \"recomendacoes\": [");
        for (int i = 0; i < r.Recommendations.Count; i++)
            sb.AppendLine($"    \"{Esc(r.Recommendations[i].Text)}\"{(i < r.Recommendations.Count - 1 ? "," : "")}");
        sb.AppendLine("  ],");

        sb.AppendLine("  \"riscos\": [");
        for (int i = 0; i < r.Risks.Count; i++)
            sb.AppendLine($"    {{ \"nome\": \"{Esc(r.Risks[i].Name)}\", \"nivel\": \"{Esc(r.Risks[i].LevelLabel)}\" }}{(i < r.Risks.Count - 1 ? "," : "")}");
        sb.AppendLine("  ]");

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Esc(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", " ")
        .Replace("\r", "");
}
