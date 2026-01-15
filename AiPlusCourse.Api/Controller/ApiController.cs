using Microsoft.AspNetCore.Mvc;
namespace AiPlusCourse.Api.Controller;

[ApiController]
[Route("[controller]")]
public class ApiController : ControllerBase
{
	/// <summary>
	/// 测试接口
	/// </summary>
	/// <returns></returns>
	[HttpGet]
	public async Task<ActionResult<string>> TestAsync()
	{
		return Ok("Hello World");
	}
}
