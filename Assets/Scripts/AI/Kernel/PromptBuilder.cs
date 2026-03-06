using System.Text;

/// <summary>
/// Semantic Kernel 스타일 프롬프트 빌더.
/// 역할, 도구, 규칙, 전략을 조합하여 시스템 프롬프트를 자동 생성.
/// ToolRegistry에서 도구 설명과 JSON 형식을 자동으로 가져옴.
/// </summary>
public class PromptBuilder
{
    string role;
    string rules;
    string strategy;
    ToolRegistry toolRegistry;

    /// <summary>AI 역할 설명 설정</summary>
    public PromptBuilder SetRole(string role)
    {
        this.role = role;
        return this;
    }

    /// <summary>중요 규칙 설정</summary>
    public PromptBuilder SetRules(string rules)
    {
        this.rules = rules;
        return this;
    }

    /// <summary>일반적인 전략 가이드 설정</summary>
    public PromptBuilder SetStrategy(string strategy)
    {
        this.strategy = strategy;
        return this;
    }

    /// <summary>도구 레지스트리 연결 (도구 설명 + JSON 형식 자동 생성)</summary>
    public PromptBuilder SetTools(ToolRegistry registry)
    {
        this.toolRegistry = registry;
        return this;
    }

    /// <summary>
    /// 최종 시스템 프롬프트 생성.
    /// 역할 → 도구 목록 → 규칙 → 전략 → JSON 형식 → 출력 규칙 순서로 조합.
    /// </summary>
    public string Build()
    {
        var sb = new StringBuilder();

        // 1. 역할 설명
        if (!string.IsNullOrEmpty(role))
        {
            sb.AppendLine(role);
            sb.AppendLine();
        }

        // 2. 도구 목록 (ToolRegistry에서 자동 생성)
        if (toolRegistry != null)
        {
            sb.Append(toolRegistry.GenerateToolList());
            sb.AppendLine();
        }

        // 3. 규칙
        if (!string.IsNullOrEmpty(rules))
        {
            sb.AppendLine(rules);
            sb.AppendLine();
        }

        // 4. 전략 가이드
        if (!string.IsNullOrEmpty(strategy))
        {
            sb.AppendLine(strategy);
            sb.AppendLine();
        }

        // 5. JSON 형식 예제 (ToolRegistry에서 자동 생성)
        if (toolRegistry != null)
        {
            sb.Append(toolRegistry.GenerateJsonFormats());
        }

        // 6. 출력 규칙
        sb.AppendLine("중요: 유효한 JSON만 출력하세요. 마크다운, 설명문, 코드 블록은 사용하지 마세요.");

        return sb.ToString();
    }
}
