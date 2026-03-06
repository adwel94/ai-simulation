using System.Collections.Generic;
using System.Text;

/// <summary>
/// 도구 파라미터 타입
/// </summary>
public enum ToolParamType
{
    String,
    Float
}

/// <summary>
/// 도구 파라미터 정의 (Semantic Kernel의 KernelParameterMetadata에 해당)
/// </summary>
[System.Serializable]
public class ToolParam
{
    public string name;
    public ToolParamType type;
    public string description;
    public string[] allowedValues;
    public float minValue;
    public float maxValue;
    public bool required = true;

    public ToolParam(string name, ToolParamType type, string description = "")
    {
        this.name = name;
        this.type = type;
        this.description = description;
    }

    public ToolParam WithAllowed(params string[] values) { allowedValues = values; return this; }
    public ToolParam WithRange(float min, float max) { minValue = min; maxValue = max; return this; }
    public ToolParam Optional() { required = false; return this; }
}

/// <summary>
/// 도구 정의 (Semantic Kernel의 KernelFunction에 해당)
/// </summary>
[System.Serializable]
public class ToolDefinition
{
    public string name;
    public string description;
    public List<ToolParam> parameters = new List<ToolParam>();
    public string jsonFormat;      // JSON 형식 예제
    public string formatNote;      // 추가 설명 (예: "direction은 left, right 중 하나")
}

/// <summary>
/// Semantic Kernel 스타일 도구 레지스트리.
/// 모든 도구를 구조화하여 관리하고, 프롬프트 텍스트를 자동 생성.
/// </summary>
public class ToolRegistry
{
    readonly List<ToolDefinition> tools = new List<ToolDefinition>();

    public IReadOnlyList<ToolDefinition> Tools => tools;

    /// <summary>도구 등록 (빌더 패턴)</summary>
    public ToolRegistry Register(ToolDefinition tool)
    {
        tools.Add(tool);
        return this;
    }

    /// <summary>이름으로 도구 조회</summary>
    public ToolDefinition GetTool(string name)
    {
        return tools.Find(t => t.name == name);
    }

    /// <summary>
    /// 도구 목록 텍스트 생성 (시스템 프롬프트의 "사용 가능한 도구" 섹션)
    /// </summary>
    public string GenerateToolList()
    {
        var sb = new StringBuilder();
        sb.AppendLine("사용 가능한 도구(가상 버튼):");

        foreach (var tool in tools)
        {
            sb.AppendLine($"- {tool.name}: {tool.description}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// JSON 형식 예제 텍스트 생성 (시스템 프롬프트의 "응답 형식" 섹션)
    /// </summary>
    public string GenerateJsonFormats()
    {
        var sb = new StringBuilder();
        sb.AppendLine("반드시 아래 형식 중 하나의 JSON 객체만 응답하세요:");
        sb.AppendLine();

        foreach (var tool in tools)
        {
            if (string.IsNullOrEmpty(tool.jsonFormat))
                continue;

            sb.AppendLine(tool.jsonFormat);
            if (!string.IsNullOrEmpty(tool.formatNote))
                sb.AppendLine(tool.formatNote);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 도구 정의를 JSON 스키마로 내보내기 (데이터셋 메타데이터용)
    /// </summary>
    public string ExportSchema()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[");

        for (int i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            sb.AppendLine($"  {{");
            sb.AppendLine($"    \"name\": \"{tool.name}\",");
            sb.AppendLine($"    \"description\": \"{EscapeJson(tool.description)}\",");
            sb.Append($"    \"parameters\": [");

            for (int j = 0; j < tool.parameters.Count; j++)
            {
                var p = tool.parameters[j];
                sb.Append($"{{\"name\":\"{p.name}\",\"type\":\"{p.type}\"");
                if (p.allowedValues != null && p.allowedValues.Length > 0)
                    sb.Append($",\"allowed\":[\"{string.Join("\",\"", p.allowedValues)}\"]");
                if (p.type == ToolParamType.Float && (p.minValue != 0 || p.maxValue != 0))
                    sb.Append($",\"range\":[{p.minValue},{p.maxValue}]");
                sb.Append("}");
                if (j < tool.parameters.Count - 1) sb.Append(",");
            }

            sb.AppendLine("]");
            sb.Append($"  }}");
            if (i < tools.Count - 1) sb.Append(",");
            sb.AppendLine();
        }

        sb.AppendLine("]");
        return sb.ToString();
    }

    static string EscapeJson(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}
