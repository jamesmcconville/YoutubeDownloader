﻿using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TagLib;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Services
{
    public class TaggingService
    {
        private readonly HttpClient _httpClient = new HttpClient();

        private readonly SemaphoreSlim _requestRateSemaphore = new SemaphoreSlim(1, 1);
        private DateTimeOffset _lastRequestInstant = DateTimeOffset.MinValue;

        public TaggingService()
        {
            // MusicBrainz requires user agent to be set
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{App.Name} ({App.GitHubProjectUrl})");
        }

        private async Task MaintainRateLimitAsync(TimeSpan interval, CancellationToken cancellationToken)
        {
            // Gain lock
            await _requestRateSemaphore.WaitAsync(cancellationToken);

            try
            {
                // Wait until enough time has passed since last request
                var timePassedSinceLastRequest = DateTimeOffset.Now - _lastRequestInstant;
                var remainingTime = interval - timePassedSinceLastRequest;
                if (remainingTime > TimeSpan.Zero)
                    await Task.Delay(remainingTime, cancellationToken);

                _lastRequestInstant = DateTimeOffset.Now;
            }
            finally
            {
                // Release the lock
                _requestRateSemaphore.Release();
            }
        }

        private async Task<JToken?> TryGetTagsJsonAsync(string artist, string title, CancellationToken cancellationToken)
        {
            var url = Uri.EscapeUriString(
                "http://musicbrainz.org/ws/2/recording/?fmt=json&query=" +
                $"artist:\"{artist}\" AND recording:\"{title}\"");

            try
            {
                // 4 requests per second
                await MaintainRateLimitAsync(TimeSpan.FromSeconds(1.0 / 4), cancellationToken);

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                var raw = await response.Content.ReadAsStringAsync();

                return JToken.Parse(raw)["recordings"]?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private async Task<IPicture?> TryGetPictureAsync(Video video, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.GetAsync(video.Thumbnails.HighResUrl,
                    HttpCompletionOption.ResponseContentRead, cancellationToken);

                var data = await response.Content.ReadAsByteArrayAsync();

                return new Picture(new ReadOnlyByteVector(data));
            }
            catch
            {
                return null;
            }
        }

        private bool TryExtractArtistAndTitle(string videoTitle, out string? artist, out string? title)
        {
            // Get rid of common rubbish in music video titles
            videoTitle = videoTitle.Replace("(official video)", "", StringComparison.OrdinalIgnoreCase);
            videoTitle = videoTitle.Replace("(official lyric video)", "", StringComparison.OrdinalIgnoreCase);
            videoTitle = videoTitle.Replace("(official music video)", "", StringComparison.OrdinalIgnoreCase);
            videoTitle = videoTitle.Replace("(official audio)", "", StringComparison.OrdinalIgnoreCase);
            videoTitle = videoTitle.Replace("(official)", "", StringComparison.OrdinalIgnoreCase);
            videoTitle = videoTitle.Replace("(lyric video)", "", StringComparison.OrdinalIgnoreCase);
            videoTitle = videoTitle.Replace("(lyrics)", "", StringComparison.OrdinalIgnoreCase);
            videoTitle = videoTitle.Replace("(acoustic video)", "", StringComparison.OrdinalIgnoreCase);
            videoTitle = videoTitle.Replace("(acoustic)", "", StringComparison.OrdinalIgnoreCase);
            videoTitle = videoTitle.Replace("(live)", "", StringComparison.OrdinalIgnoreCase);
            videoTitle = videoTitle.Replace("(animated video)", "", StringComparison.OrdinalIgnoreCase);

            // Split by common artist/title separator characters
            var split = videoTitle.Split(new[] {" - ", " ~ ", " — ", " – "}, StringSplitOptions.RemoveEmptyEntries);

            // Extract artist and title
            if (split.Length >= 2)
            {
                artist = split[0].Trim();
                title = split[1].Trim();
                return true;
            }

            if (split.Length == 1)
            {
                artist = null;
                title = split[0].Trim();
                return true;
            }

            artist = null;
            title = null;
            return false;
        }

        public async Task InjectTagsAsync(Video video, string format, string filePath, CancellationToken cancellationToken)
        {
            // Only audio files are supported at the moment
            if (!string.Equals(format, "mp3", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(format, "ogg", StringComparison.OrdinalIgnoreCase))
                return;

            // Try to extract artist/title from video title
            if (!TryExtractArtistAndTitle(video.Title, out var artist, out var title))
                return;

            // Try to get tags
            var tagsJson = await TryGetTagsJsonAsync(artist!, title!, cancellationToken);

            // Try to get picture
            var picture = await TryGetPictureAsync(video, cancellationToken);

            // Extract information
            var resolvedArtist = tagsJson?["artist-credit"]?.FirstOrDefault()?["name"]?.Value<string>();
            var resolvedTitle = tagsJson?["title"]?.Value<string>();
            var resolvedAlbumName = tagsJson?["releases"]?.FirstOrDefault()?["title"]?.Value<string>();

            // Inject tags
            var taggedFile = File.Create(filePath);
            taggedFile.Tag.Performers = new[] {resolvedArtist ?? artist ?? ""};
            taggedFile.Tag.Title = resolvedTitle ?? title ?? "";
            taggedFile.Tag.Album = resolvedAlbumName ?? "";
            taggedFile.Tag.Pictures = picture != null ? new[] {picture} : Array.Empty<IPicture>();
            taggedFile.Save();
        }
    }
}