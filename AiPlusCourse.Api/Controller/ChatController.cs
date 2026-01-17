using Microsoft.AspNetCore.Mvc;
using System.Text;
namespace AiPlusCourse.Api.Controller;

[Route("api/[controller]")]
[ApiController]
public class ChatController : ControllerBase
{
    // 定义请求模型
    public class ChatRequest
    {
        public string Message { get; set; }
    }

    [HttpPost("stream")]
    public async Task Stream([FromBody] ChatRequest request)
    {
        // 1. 设置响应头，告诉浏览器这是一个流
        Response.ContentType = "text/plain"; 
        // 如果你是做标准 SSE，可以用 "text/event-stream"，但你前端是直接读流，text/plain 也可以
            
        // 禁用缓存
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        try
        {
            // 获取用户输入
            var userMessage = request?.Message ?? "";

            // --- 模拟 AI 的回复内容 (之后这里替换为真实的 Gemini API 调用) ---
            var aiResponseText = $"[后端回复] 我收到了你的消息：{userMessage}。\n\n" +
                                 "这是一段来自 .NET API 的流式响应测试。\n" +
                                 "后端正在逐字生成内容... \n" +
                                 "10%... \n" +
                                 "50%... \n" +
                                 "100% 完成！🚀";

            // --- 开始流式输出 ---
            // 我们把字符串拆成字符，模拟打字机效果
            foreach (var character in aiResponseText)
            {
                // 将字符转换为字节
                var buffer = Encoding.UTF8.GetBytes(character.ToString());

                // 写入响应流
                await Response.Body.WriteAsync(buffer, 0, buffer.Length);
                    
                // 关键：立即刷新缓冲区，让前端能马上收到，而不是等攒够了一起发
                await Response.Body.FlushAsync();

                // 模拟思考延迟 (50毫秒)
                await Task.Delay(50); 
            }
        }
        catch (Exception ex)
        {
            // 错误处理
            var errorMsg = Encoding.UTF8.GetBytes($"\n[Error] {ex.Message}");
            await Response.Body.WriteAsync(errorMsg, 0, errorMsg.Length);
        }
    }
}