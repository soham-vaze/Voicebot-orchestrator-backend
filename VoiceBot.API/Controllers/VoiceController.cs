using Microsoft.AspNetCore.Mvc;
using VoiceBot.Application.Services;
using VoiceBot.Domain.Exceptions;
using VoiceBot.Domain.Models;

namespace VoiceBot.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VoiceController : ControllerBase
{
    private readonly IVoiceOrchestrator _orchestrator;
    private readonly ILogger<VoiceController> _logger;

    public VoiceController(
        IVoiceOrchestrator orchestrator,
        ILogger<VoiceController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Accepts an audio file and returns the LLM text reply + TTS audio (base64).
    /// POST /api/voice/process
    /// </summary>
    [HttpPost("process")]
    public async Task<IActionResult> ProcessAudio(IFormFile file, [FromForm] string? sessionId = null)
    {
        // ------------------------------------------------------------------
        // 📨 Request Validation
        // ------------------------------------------------------------------
        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("⚠️ No audio file provided in request");
            return BadRequest(new { error = "No audio file provided." });
        }

        _logger.LogInformation("🎤 Received audio file: {FileName}, Size: {Size} bytes, SessionId: {SessionId}",
            file.FileName, file.Length, sessionId ?? "(new)");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);

        var request = new AudioRequest
        {
            AudioData = memoryStream.ToArray(),
            FileName = file.FileName,
            SessionId = sessionId,   // null ⇒ orchestrator will generate a new session
        };
f
        try
        {
            _logger.LogInformation("⚙️ Sending audio to orchestrator");

            var (query, text, audio) = await _orchestrator.ProcessAudioAsync(request);

            stopwatch.Stop();

            _logger.LogInformation(
                "✅ Processing completed in {ElapsedMs} ms | Text length: {TextLength} | Audio bytes: {AudioSize}",
                stopwatch.ElapsedMilliseconds,
                text?.Length ?? 0,
                audio?.Length ?? 0
            );

            return Ok(new
            {
                sessionId,                // echo back so the frontend can reuse it
                text,
                audioBase64 = Convert.ToBase64String(audio ?? Array.Empty<byte>()),
            });
        }
        catch (PipelineException ex)
        {
            stopwatch.Stop();

            _logger.LogWarning(
                "⚠️ PipelineException at stage {Stage}: {Detail} | Time: {ElapsedMs} ms",
                ex.Stage,
                ex.Detail,
                stopwatch.ElapsedMilliseconds
            );

            return BadRequest(new
            {
                stage = ex.Stage,
                detail = ex.Detail,
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex,
                "❌ Unexpected error while processing audio | Time: {ElapsedMs} ms",
                stopwatch.ElapsedMilliseconds
            );

            throw; // let ASP.NET handle 500
        }
    }
}