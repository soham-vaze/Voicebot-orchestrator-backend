using Microsoft.AspNetCore.Mvc;
using VoiceBot.Application.Services;
using VoiceBot.Domain.Models;

namespace VoiceBot.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VoiceController : ControllerBase
{
    private readonly IVoiceOrchestrator _orchestrator;

    public VoiceController(IVoiceOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessAudio(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No audio file provided.");

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);

        var request = new AudioRequest
        {
            AudioData = memoryStream.ToArray(),
            FileName = file.FileName
        };

        var (text, audio) = await _orchestrator.ProcessAudioAsync(request);

        return Ok(new
        {
            text,
            audioBase64 = Convert.ToBase64String(audio)
        });
    }
}
