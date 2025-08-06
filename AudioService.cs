// AudioService.cs
// Servicio para descargar y reproducir archivos de audio.

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NAudio.Wave;

public class AudioService
{
    private readonly HttpClient _httpClient = new();

    /// <summary>
    /// Descarga un archivo de audio desde una URL y lo reproduce de forma asíncrona.
    /// </summary>
    /// <param name="url">La URL del archivo de audio (ej. MP3).</param>
    public async Task PlaySoundFromUrl(string url)
    {
        try
        {
            Console.WriteLine($"[AudioService]: Descargando audio desde {url}...");
            byte[] audioBytes = await _httpClient.GetByteArrayAsync(url);

            using (var memoryStream = new System.IO.MemoryStream(audioBytes))
            using (var mp3Reader = new Mp3FileReader(memoryStream))
            using (var waveOut = new WaveOutEvent())
            {
                // --- ¡NUEVA LÍNEA PARA CONTROLAR EL VOLUMEN! ---
                // 1.0f = 100% volumen, 0.5f = 50% volumen, 0.0f = silencio.
                waveOut.Volume = 0.5f;
                // ------------------------------------------------

                Console.WriteLine($"[AudioService]: Reproduciendo sonido al {waveOut.Volume * 100}% de volumen...");
                waveOut.Init(mp3Reader);
                waveOut.Play();

                while (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    await Task.Delay(500);
                }
                Console.WriteLine("[AudioService]: Reproducción finalizada.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioService ERROR]: No se pudo reproducir el sonido desde la URL. Razón: {ex.Message}");
        }
    }
    
}