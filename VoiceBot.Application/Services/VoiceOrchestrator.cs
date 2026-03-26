using System.Text;
using Microsoft.Extensions.Logging;
    using VoiceBot.Application.Interfaces;
    using VoiceBot.Domain.Exceptions;
    using VoiceBot.Domain.Models;

    namespace VoiceBot.Application.Services;

    /// <summary>
    /// Orchestrates the voice processing pipeline via the injected ILlmBackend.
    ///
    /// Why inject ILlmBackend and not a concrete type?
    ///   Program.cs registers either FastPipelineBackend or WhatsAppTeamBackend based on
    ///   "PipelineMode" in config. This class never changes regardless of which backend is active.
    ///
    /// Clean Architecture rule: ILogger is the only "infrastructure-ish" type allowed here
    /// because it's a first-class .NET abstraction. No HttpClient, no JSON, no DB contexts.
    /// </summary>
    public class VoiceOrchestrator : IVoiceOrchestrator
    {
        private readonly ILlmBackend _backend;
        private readonly ILogger<VoiceOrchestrator> _logger;

        // Threshold above which the total pipeline latency is flagged at Warning level.
        private const int HighLatencyThresholdMs = 5_000;

        public VoiceOrchestrator(ILlmBackend backend, ILogger<VoiceOrchestrator> logger)
        {
            _backend = backend;
            _logger  = logger;
        }

        public async Task<(string query, string text, byte[] audio)> ProcessAudioAsync(AudioRequest request)
        {
            // Use the session ID supplied by the caller (frontend) to maintain conversation
            // continuity across requests. Fall back to a new Guid only when no ID is provided
            // (e.g. first request in a new session, or non-browser callers like healthcheck.py).
            var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
                ? Guid.NewGuid().ToString()
                : request.SessionId;

            _logger.LogInformation(
                "VoiceOrchestrator: starting session={Session}, file={File}, bytes={Bytes}",
                sessionId, request.FileName, request.AudioData.Length);

            LlmBackendResponse result;
            try
            {
                // string audiobytestostring = Encoding.UTF8.GetString(request.AudioData);
                result = await _backend.ProcessAsync(sessionId, request.AudioData, request.FileName);
            }
            catch (PipelineException)
            {
                // Re-throw so the controller can catch it and return a structured 400.
                // We don't wrap it because PipelineException already carries stage + detail.
                throw;
            }
            catch (Exception ex)
            {
                // Unexpected exceptions (not PipelineException) should not leak raw stack traces.
                // Wrap them into a PipelineException at the "orchestrator" stage so the same
                // controller catch-block handles them uniformly.
                _logger.LogError(ex, "VoiceOrchestrator: unexpected error during processing.");
                throw new PipelineException("orchestrator", ex.Message, ex);
            }

            // ------------------------------------------------------------------
            // Latency logging
            // ------------------------------------------------------------------
            LogLatency(sessionId, result.LatencyMs);

            _logger.LogInformation(
                "VoiceOrchestrator: completed session={Session}, transcript='{Transcript}', format={Format}",
                sessionId, result.Transcript, result.AudioFormat);

            // Decode the base64 audio string back to bytes for the response model.
            byte[] audioBytes = string.IsNullOrWhiteSpace(result.AudioBase64)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(result.AudioBase64);

            // Return the LLM's response text (not the transcript) as the "text" field,
            // because that is what the end-user hears read back to them.
            return (result.Transcript, result.ResponseText, audioBytes);
        }

        // ---------------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------------

        private void LogLatency(string sessionId, Dictionary<string, int> latencyMs)
        {
            if (latencyMs.Count == 0)
            {
                _logger.LogInformation("VoiceOrchestrator: session={Session} — no latency data.", sessionId);
                return;
            }

            latencyMs.TryGetValue("stt",      out int stt);
            latencyMs.TryGetValue("db_fetch", out int dbFetch);
            latencyMs.TryGetValue("llm",      out int llm);
            latencyMs.TryGetValue("db_write", out int dbWrite);
            latencyMs.TryGetValue("tts",      out int tts);
            latencyMs.TryGetValue("total",    out int total);

            var logLevel = total > HighLatencyThresholdMs
                ? LogLevel.Warning
                : LogLevel.Information;

            _logger.Log(
                logLevel,
                "VoiceOrchestrator: session={Session} latency — " +
                "stt={Stt}ms db_fetch={DbFetch}ms llm={Llm}ms db_write={DbWrite}ms tts={Tts}ms total={Total}ms",
                sessionId, stt, dbFetch, llm, dbWrite, tts, total);

            if (total > HighLatencyThresholdMs)
                _logger.LogWarning(
                    "VoiceOrchestrator: session={Session} total latency {Total}ms exceeded threshold {Threshold}ms.",
                    sessionId, total, HighLatencyThresholdMs);
        }
    }
