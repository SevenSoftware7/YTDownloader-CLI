using System.CommandLine;
using AngleSharp.Text;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace YTDL;


class Program {

    static async Task<int> Main(string[] args) {
        Option<string> format = new("--format", getDefaultValue: () => "mp4", description: "The format of the output file(s)");
        Option<string[]> urls = new("--video", getDefaultValue: () => [], description: "The url(s) from which to download the video(s)");
        Option<string[]> playlistUrls = new("--playlist", getDefaultValue: () => [], description: "The url(s) of playlists from which to download the video(s)");
        Option<DirectoryInfo> outputDirectory = new("--output", getDefaultValue: () => new("./output/"), description: "The directory in which the downloaded videos will be placed");
        Option<FileInfo> ffmpegFile = new("--ffmpeg", getDefaultValue: () => new("./ffmpeg.exe"), description: "The location of FFMPEG, used to convert the downloaded video to the desired format");

        RootCommand rootCommand = new("Download youtube videos/playlists") {
            format,
            urls,
            playlistUrls,
            outputDirectory,
            ffmpegFile
        };

        rootCommand.SetHandler( Execute, format, urls, playlistUrls, outputDirectory, ffmpegFile);

        return await rootCommand.InvokeAsync(args);
    }

    private async static Task Execute(string format, string[] urls, string[] playlistUrls, DirectoryInfo outputDirectory, FileInfo ffmpegDirectory) {

        Directory.CreateDirectory(outputDirectory.FullName);

        if ( ! ffmpegDirectory.Exists ) {
            Console.WriteLine("Directory of FFMPEG required.");
            return;
        }

        bool isConsoleFed = false;
        if (urls.Length == 0 && playlistUrls.Length == 0) {
            Console.WriteLine("Input URLs of videos to download (separated by space ( )), can be left empty");
            urls = Console.ReadLine()?.SplitWithTrimming(' ') ?? [];

            Console.WriteLine("Input URLs of playlists to download (separated by space ( )), can be left empty");
            playlistUrls = Console.ReadLine()?.SplitWithTrimming(' ') ?? [];

            isConsoleFed = true;
        }

        List<IVideo> videos = [];
        YoutubeClient? youtube = new();

        if (playlistUrls.Length != 0) {
            try {
                foreach (string playlistUrl in playlistUrls) {
                    Playlist playlist = await youtube.Playlists.GetAsync(playlistUrl);
                    IAsyncEnumerable<PlaylistVideo> vids = youtube.Playlists.GetVideosAsync(playlist.Id);
                    await foreach (PlaylistVideo video in vids) {
                        videos.Add(video);
                    }
                }
            } catch (Exception e) {
                Console.WriteLine($"Error while decoding playlist : {e.Message}");
            }
        }
        if (urls.Length != 0) {
            try {
                foreach (string videoUrl in urls) {
                    Video video = await youtube.Videos.GetAsync(videoUrl);
                    videos.Add(video);
                }
            } catch (Exception e) {
                Console.WriteLine($"Error while decoding urls : {e.Message}");
            }
        }

        uint succeedCount = 0;
        foreach (IVideo video in videos) {
            try {
                await DownloadYouTubeVideo(video, outputDirectory, ffmpegDirectory, format);
                succeedCount++;
            } catch (Exception e) {
                Console.WriteLine("An error occurred while downloading the videos: " + e.Message);
            }
        }

        Console.WriteLine($"{(succeedCount == 1 ? "1 video was" : $"{succeedCount} videos were")} downloaded!");
        if (isConsoleFed) {
            Console.ReadLine();
        }
    }
    static async Task DownloadYouTubeVideo(IVideo video, DirectoryInfo outputDirectory, FileInfo ffmpegFile, string format) {
        YoutubeClient? youtube = new();

        // Sanitize the video title to remove invalid characters from the file name
        string sanitizedTitle = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));

        string outputFilePath = Path.Combine(outputDirectory.FullName, $"{sanitizedTitle}.{format}");

        await youtube.Videos.DownloadAsync(video.Url, outputFilePath, o => o
            .SetContainer(format)
            .SetPreset(ConversionPreset.VerySlow)
            .SetFFmpegPath(ffmpegFile.FullName)
        );

        Console.WriteLine("Download completed!");

        Console.WriteLine($"Video saved as: {outputFilePath}");
    }
}
