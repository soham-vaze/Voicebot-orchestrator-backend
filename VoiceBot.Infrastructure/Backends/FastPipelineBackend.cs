using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VoiceBot.Application.Interfaces;
using VoiceBot.Domain.Exceptions;

namespace VoiceBot.Infrastructure.Backends;



/// <summary>
/// Calls the Python FastAPI pipeline service (STT → LLM → TTS in one round-trip).
/// All HTTP mechanics are confined here; the Application layer only sees ILlmBackend.
/// </summary>
public sealed class FastPipelineBackend : ILlmBackend
{
    // The HttpClient is pre-configured with BaseAddress and Timeout by IHttpClientFactory.
    // The named client "PythonPipeline" is registered in Program.cs.
    private readonly HttpClient _http;
    private readonly ILogger<FastPipelineBackend> _logger;

    // Shared options: snake_case property names match the Python JSON contract.
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public FastPipelineBackend(HttpClient http, ILogger<FastPipelineBackend> logger)
    {
        _http   = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LlmBackendResponse> ProcessAsync(
        string sessionId,
        byte[] audioBytes,
        string audioFileName,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "FastPipelineBackend: sending {Bytes} bytes, session={Session}, file={File}",
            audioBytes.Length, sessionId, audioFileName);

        // Build multipart/form-data matching the Python endpoint's expected form fields:
        //   - "session_id" : string
        //   - "file"       : binary audio content
        using var formContent = new MultipartFormDataContent();

        formContent.Add(new StringContent(sessionId), "session_id");

        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("audio/octet-stream");
        formContent.Add(fileContent, "audio_file", audioFileName);
        
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync("/process", formContent, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "FastPipelineBackend: HTTP connection failed.");
            throw new PipelineException("http", "The Python pipeline service is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "FastPipelineBackend: request timed out.");
            throw new PipelineException("http", "The Python pipeline service timed out.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            // Attempt to read Python's structured error body: { "stage": "...", "detail": "..." }
            // Fall back to raw body text if it doesn't parse.
            var rawError = await response.Content.ReadAsStringAsync(ct);
            string stage  = "unknown";
            string detail = rawError;

            try
            {
                var errorDoc = JsonDocument.Parse(rawError);
                if (errorDoc.RootElement.TryGetProperty("stage", out var s))
                    stage  = s.GetString() ?? stage;
                if (errorDoc.RootElement.TryGetProperty("detail", out var d))
                {
                    // Handle both string and array formats
                    if (d.ValueKind == JsonValueKind.String)
                    {
                        detail = d.GetString() ?? detail;
                    }
                    else if (d.ValueKind == JsonValueKind.Array)
                    {
                        // FastAPI validation errors come as an array - combine them
                        var errors = new System.Collections.Generic.List<string>();
                        foreach (var item in d.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                errors.Add(item.GetString() ?? "");
                            }
                            else if (item.TryGetProperty("msg", out var msg))
                            {
                                // FastAPI format: {"loc": [...], "msg": "...", "type": "..."}
                                errors.Add(msg.GetString() ?? "");
                            }
                            else
                            {
                                errors.Add(item.ToString());
                            }
                        }
                        detail = string.Join("; ", errors);
                    }
                    else
                    {
                        detail = d.ToString();
                    }
                }
            }
            catch (JsonException) { /* raw text fallback already set above */ }

            _logger.LogError(  
                "FastPipelineBackend: non-success {Status} at stage '{Stage}': {Detail}",
                (int)response.StatusCode, stage, detail);

            throw new PipelineException(stage, detail);
        }

        // Deserialize the Python response envelope.
        PythonProcessResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<PythonProcessResponse>(_jsonOptions, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "FastPipelineBackend: unexpected response shape from Python service.");
            throw new PipelineException("deserialization", "Unexpected response format from pipeline service.", ex);
        }

        if (body is null)
            throw new PipelineException("deserialization", "Python service returned an empty body.");

        return new LlmBackendResponse(
            Transcript:   body.Transcript,
            ResponseText: body.ResponseText,
            AudioBase64:  body.AudioBase64,
            AudioFormat:  body.AudioFormat,
            LatencyMs:    body.LatencyMs ?? new Dictionary<string, int>()
        );
    }

    // ---------------------------------------------------------------------------
    // Private DTO — mirrors the Python /process response shape.
    // Kept internal to Infrastructure so no other layer depends on the wire format.
    // ---------------------------------------------------------------------------
    private sealed class PythonProcessResponse
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;

        [JsonPropertyName("transcript")]
        public string Transcript { get; set; } = string.Empty;

        [JsonPropertyName("response_text")]
        public string ResponseText { get; set; } = string.Empty;

        [JsonPropertyName("audio_base64")]
        public string AudioBase64 { get; set; } = string.Empty;

        [JsonPropertyName("audio_format")]
        public string AudioFormat { get; set; } = "mp3";

        [JsonPropertyName("latency_ms")]
        public Dictionary<string, int>? LatencyMs { get; set; }
    }
}
