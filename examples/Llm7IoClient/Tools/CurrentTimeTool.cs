using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Andy.Model.Model;
using Andy.Model.Tooling;

namespace Llm7IoClient.Tools;

public class CurrentTimeTool : ITool
{
    public ToolDeclaration Definition => new ToolDeclaration
    {
        Name = "get_current_time",
        Description = "Get the current time",
        Parameters = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>()
        }
    };

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        var currentTime = DateTime.Now;

        return Task.FromResult(ToolResult.FromObject(call.Id, call.Name, new
        {
            time = currentTime.ToString("yyyy-MM-dd HH:mm:ss"),
            timezone = TimeZoneInfo.Local.DisplayName,
            unix_timestamp = ((DateTimeOffset)currentTime).ToUnixTimeSeconds()
        }));
    }
}