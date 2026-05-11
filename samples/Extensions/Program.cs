// SDR domain via custom `arcpx.sdr.*.v1` extension messages.
//
// Tune to 145.500 MHz (2 m FM calling), capture 5 s of IQ at 2.048 MS/s,
// NBFM-demodulate to 48 kHz PCM. Exercises §21 naming, capability
// advertisement, and unknown-message handling.
using System.Text.Json;
using ARCP.Client;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Messages.Session;
using ARCP.Samples.Extensions;
using static ARCP.Samples.Extensions.ClientStubs;
using Env = ARCP.Envelope.Envelope;

const string ExtTune = "arcpx.sdr.tune.v1";
const string ExtGain = "arcpx.sdr.gain.v1";
const string ExtCapture = "arcpx.sdr.capture.v1";
const string ExtDemodulate = "arcpx.sdr.demodulate.v1";
string[] allExtensions = [ExtTune, ExtGain, ExtCapture, ExtDemodulate];

// capabilities.extensions=allExtensions on the open call.
ARCPClient client = null!;
SessionAccepted accepted = await Open(client);

// If the runtime didn't advertise our required extension set, refuse the
// session — RFC §7 / §21.2.
HashSet<string> advertised = new(accepted.Capabilities?.Extensions ?? []);
if (!allExtensions.All(advertised.Contains))
{
    throw new ARCPException(
        ErrorCode.Unimplemented,
        $"runtime missing SDR extensions: [{string.Join(", ", advertised)}]");
}

string handle = Guid.NewGuid().ToString("N")[..8];

await Request(client, Envelope(client, ExtTune, new ExtensionPayload(JsonSerializer.SerializeToElement(new
{
    center_freq_hz = 145_500_000.0,
    sample_rate_hz = 2_048_000.0,
    ppm_correction = 1,
}))), timeout: TimeSpan.FromSeconds(10));

await Request(client, Envelope(client, ExtGain, new ExtensionPayload(JsonSerializer.SerializeToElement(new
{
    stages = new[] { new { name = "TUNER", value_db = 28.0 } },
}))), timeout: TimeSpan.FromSeconds(10));

// Capture returns an artifact.ref pointing at the IQ buffer. The buffer
// never travels inline — demodulate references it.
Env cap = await Request(
    client,
    Envelope(client, ExtCapture, new ExtensionPayload(JsonSerializer.SerializeToElement(new
    {
        seconds = 5.0,
        capture_handle = handle,
        decimate = 1,
    }))),
    timeout: TimeSpan.FromSeconds(15));
JsonElement capPayload = ((ExtensionPayload)cap.Payload).Value;
string iqArtifact = capPayload.GetProperty("artifact_id").GetString()!;
Console.WriteLine($"captured IQ → {iqArtifact}");

Env audio = await Request(
    client,
    Envelope(client, ExtDemodulate, new ExtensionPayload(JsonSerializer.SerializeToElement(new
    {
        iq_artifact_id = iqArtifact,
        mode = "NBFM",
        audio_rate_hz = 48_000,
    }))),
    timeout: TimeSpan.FromSeconds(15));
JsonElement audioPayload = ((ExtensionPayload)audio.Payload).Value;
Console.WriteLine($"demod  PCM → {audioPayload.GetProperty("artifact_id").GetString()}");

// §21.3 demonstration: unadvertised extension marked optional. Runtime
// SHOULD ack (silent drop) rather than nack.
Env optional = await Request(
    client,
    Envelope(
        client,
        "arcpx.sdr.experimental_doppler.v1",
        new ExtensionPayload(JsonSerializer.SerializeToElement(new { velocity_mps = 7.4 })),
        extensions: new Dictionary<string, JsonElement>
        {
            ["optional"] = JsonSerializer.SerializeToElement(true),
        }),
    timeout: TimeSpan.FromSeconds(5));
Console.WriteLine($"optional unknown → {optional.Type}");

await client.CloseAsync();
