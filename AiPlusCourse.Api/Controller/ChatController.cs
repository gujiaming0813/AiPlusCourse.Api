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
        // 1. 设置响应头：纯文本流
        Response.ContentType = "text/plain";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        // 2. 准备请求数据 (完全复用你的逻辑)
        var url = configuration["Url"]!;

        var body = new
                   {
                       input = new
                               {
                                   prompt = request.Message,
                                   session_id = request.SessionId,
                                   biz_params = new
                                                {
                                                    user_prompt_params = new
                                                                         {
                                                                             level = request.Level
                                                                         }
                                                },
                                   parameters = new
                                                {
                                                    incremental_output = true,
                                                    has_thoughts = true
                                                },
                                   debug = new
                                           {
                                           },
                               }
                   };

        // 3. 发起请求
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {configuration["Key"]}");
        client.DefaultRequestHeaders.Add("X-DashScope-SSE", $"enable");

        string jsonContent = JsonConvert.SerializeObject(body);
        HttpContent content = new StringContent(jsonContent,
                                                Encoding.UTF8,
                                                "application/json");

        var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, url);
        upstreamRequest.Content = content;

        // 4. 获取流式响应
        // ResponseHeadersRead: 只要头返回了就开始读，不要等整个 Body
        using var response = await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var errorMsg = await response.Content.ReadAsStringAsync();
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"[Error] {response.StatusCode}: {errorMsg}"));
            return;
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? line;
        string lastText = "";
        var isHeaderSet = false;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("data:"))
            {
                var dataJson = line.Substring(5).Trim();
                if (string.IsNullOrEmpty(dataJson)) continue;

                try
                {
                    var jsonNode = JsonNode.Parse(dataJson);

                    if (!isHeaderSet && !Response.HasStarted)
                    {
                        var sessionId = jsonNode?["output"]?["session_id"]?.ToString();
                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            // 允许前端读取这个 Header
                            Response.Headers["Access-Control-Expose-Headers"] = "X-Session-Id";
                            Response.Headers["X-Session-Id"] = sessionId;
                            isHeaderSet = true;
                            // Console.WriteLine($"[Debug] SessionId set: {sessionId}"); // 调试用
                        }
                    }

                    // 获取当前的全量文本
                    var currentFullText = jsonNode?["output"]?["text"]?.ToString() ?? "";

                    // 👇 2. 计算增量 (Delta)
                    // 如果当前全量文本比上一次的长，说明有新内容
                    if (currentFullText.Length > lastText.Length)
                    {
                        // 截取掉前面已经发过的部分，只留新多出来的部分
                        var delta = currentFullText.Substring(lastText.Length);

                        // 更新“上次内容”为“当前内容”，为下一次做准备
                        lastText = currentFullText;

                        // 👇 3. 只发送增量给前端
                        if (!string.IsNullOrEmpty(delta))
                        {
                            var buffer = Encoding.UTF8.GetBytes(delta);
                            await Response.Body.WriteAsync(buffer);
                            await Response.Body.FlushAsync();
                        }
                    }

                    var finishReason = jsonNode?["output"]?["finish_reason"]?.ToString();
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