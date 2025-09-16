using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Model.Model;
using Andy.Model.Tooling;

namespace Llm7IoClient.Tools;

public class FibonacciTool : ITool
{
    public ToolDeclaration Definition => new ToolDeclaration
    {
        Name = "calculate_fibonacci",
        Description = "Calculate the nth fibonacci number",
        Parameters = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["n"] = new Dictionary<string, object>
                {
                    ["type"] = "integer",
                    ["description"] = "The position in the fibonacci sequence"
                }
            },
            ["required"] = new[] { "n" }
        }
    };

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        try
        {
            var args = JsonDocument.Parse(call.ArgumentsJson);
            var n = args.RootElement.GetProperty("n").GetInt32();

            var result = CalculateFibonacci(n);

            return Task.FromResult(ToolResult.FromObject(call.Id, call.Name,
                new { result, position = n, message = $"The {n}th fibonacci number is {result}" }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromObject(call.Id, call.Name,
                new { error = ex.Message }, isError: true));
        }
    }

    private int CalculateFibonacci(int n)
    {
        if (n <= 0) return 0;
        if (n == 1) return 1;

        int a = 0, b = 1;
        for (int i = 2; i <= n; i++)
        {
            int temp = a + b;
            a = b;
            b = temp;
        }
        return b;
    }
}