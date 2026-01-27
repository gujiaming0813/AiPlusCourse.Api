using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json.Nodes;
namespace AiPlusCourse.Api.Controller;

[Route("api/[controller]")]
[ApiController]
public class ChatController(IHttpClientFactory httpClientFactory, IConfiguration configuration) : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IConfiguration _configuration = configuration;

    // 定义请求模型
    public class ChatRequest
    {
        public string Message { get; set; } = null!;
        public string? SessionId { get; set; }
        public int Level { get; set; } = 1;
    }

    [HttpPost("stream")]
    public async Task Stream(ChatRequest request)
    {
        var responseFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        if (responseFeature != null)
        {
            responseFeature.DisableBuffering();
        }
        // 1. 设置响应头
        Response.ContentType = "application/json";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        // 2. 准备请求数据
        var url = configuration["Url"]!;
        var jsonString = $@"{{
        ""input"": {{
            ""prompt"": ""{request.Message}"",
            ""session_id"": ""{request.SessionId}"",
            ""biz_params"": {{
                ""user_prompt_params"": {{
                    ""level"": ""{request.Level}""
                }}
            }}
        }},
        ""parameters"": {{
            ""has_thoughts"": true,
            ""enable_thinking"": true,
            ""incremental_output"": true
        }},
        ""debug"": {{}}
    }}";

        // 3. 发起请求
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {configuration["Key"]}");
        client.DefaultRequestHeaders.Add("X-DashScope-SSE", $"enable");

        var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
        var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, url)
                              {
                                  Content = content
                              };

        // 4. 获取流式响应
        using var response = await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var errorMsg = await response.Content.ReadAsStringAsync();
            // 发送错误类型的 JSON
            var errPayload = System.Text.Json.JsonSerializer.Serialize(new
                                                                       {
                                                                           type = "error",
                                                                           content = $"{response.StatusCode}: {errorMsg}"
                                                                       });
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(errPayload + "\n"));
            return;
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? line;
        // 记录上一次的正文全文
        string lastText = "";
        // 记录上一次的思考全文
        string lastThought = "";
        var isHeaderSet = false;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("data:"))
            {
                var dataJson = line.Substring(5).Trim();
                // Console.WriteLine(dataJson);
                if (string.IsNullOrEmpty(dataJson)) continue;

                try
                {
                    var jsonNode = JsonNode.Parse(dataJson);

                    // 处理 SessionId
                    if (!isHeaderSet && !Response.HasStarted)
                    {
                        var sessionId = jsonNode?["output"]?["session_id"]?.ToString();
                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            Response.Headers["Access-Control-Expose-Headers"] = "X-Session-Id";
                            Response.Headers["X-Session-Id"] = sessionId;
                            isHeaderSet = true;
                        }
                    }

                    var outputNode = jsonNode?["output"];
                    if (outputNode == null) continue;

                    // --- 处理思考过程 (Thoughts) ---
                    var currentThought = outputNode["thoughts"]?[0]?["thought"]?.ToString() ?? "";

                    // var thoughtDelta = currentThought.Substring(lastThought.Length);
                    lastThought = currentThought;

                    if (!string.IsNullOrEmpty(currentThought))
                    {
                        // 构造自定义协议: { "type": "thought", "content": "..." }
                        var payload = System.Text.Json.JsonSerializer.Serialize(new
                                                                                {
                                                                                    type = "thought",
                                                                                    content = currentThought
                                                                                });
                        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(payload + "\n"));//以此换行符为分隔
                        await Response.Body.FlushAsync();
                    }

                    // --- 处理正文回复 (Text) ---
                    var currentText = outputNode["text"]?.ToString() ?? "";
                    // var textDelta = currentText.Substring(lastText.Length);
                    lastText = currentText;

                    if (!string.IsNullOrEmpty(currentText))
                    {
                        // 构造自定义协议: { "type": "text", "content": "..." }
                        var payload = System.Text.Json.JsonSerializer.Serialize(new
                                                                                {
                                                                                    type = "text",
                                                                                    content = currentText
                                                                                });
                        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(payload + "\n"));
                        await Response.Body.FlushAsync();
                    }

                    // 检查结束
                    var finishReason = outputNode["finish_reason"]?.ToString();
                    if (finishReason == "stop")
                    {
                        break;
                    }
                }
                catch
                {
                    // 忽略解析错误
                }
            }
        }
    }
}