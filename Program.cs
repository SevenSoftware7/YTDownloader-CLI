using System.CommandLine;
using System.Runtime.InteropServices;
using AngleSharp.Text;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace YTDL;

public class Program {

	private static async Task<int> Main(string[] args) {
		Option<string> format = new("--format", getDefaultValue: () => "mp4", description: "The format of the output file(s)");
		Option<string[]> urls = new("--url", getDefaultValue: () => [], description: "The url(s) from which to download the video(s)");
		Option<DirectoryInfo> outputDirectory = new("--output", getDefaultValue: () => new("./output/"), description: "The directory in which the downloaded videos will be placed");
		Option<FileInfo> ffmpegFile = new("--ffmpeg", getDefaultValue: () => new(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "./ffmpeg.exe" : "./ffmpeg"), description: "The location of FFMPEG, used to convert the downloaded video to the desired format");

		RootCommand rootCommand = new("Download youtube videos/playlists") {
			format,
			urls,
			outputDirectory,
			ffmpegFile
		};

		rootCommand.SetHandler(Execute, format, urls, outputDirectory, ffmpegFile);

		return await rootCommand.InvokeAsync(args);
	}

	// Sanitize the video title to remove invalid characters from the file name
	private static string GetSanitizedTitle(IVideo video) =>
		string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));

	private static async Task Execute(string format, string[] urls, DirectoryInfo outputDirectory, FileInfo ffmpegDirectory) {
		Directory.CreateDirectory(outputDirectory.FullName);

		if (!ffmpegDirectory.Exists) {
			Console.WriteLine("Directory of FFMPEG required.");
			return;
		}

		string[] playlistUrls = [];
		string[] videoUrls = [];

		bool isConsoleFed = false;
		if (urls.Length == 0) {
			isConsoleFed = true;

			Console.WriteLine("Input URLs of videos/playlists to download (separated by space ( )), can be left empty");

			urls = Console.ReadLine()?.SplitWithTrimming(' ') ?? [];
		}

		HashSet<IVideo> videos = [];
		YoutubeClient? youtube = new();


		videoUrls = [.. urls.Where(url => url.Contains("v="))];
		playlistUrls = [.. urls.Except(videoUrls).Where(url => url.Contains("list="))];

		if (playlistUrls.Length != 0) {
			try {
				foreach (string playlistUrl in playlistUrls) {
					Playlist playlist = await youtube.Playlists.GetAsync(playlistUrl);
					IAsyncEnumerable<PlaylistVideo> vids = youtube.Playlists.GetVideosAsync(playlist.Id);
					await foreach (PlaylistVideo video in vids) {
						videos.Add(video);
					}
				}
			}
			catch (Exception e) {
				Console.WriteLine($"Error while decoding playlist : {e.Message}");
			}
		}
		if (videoUrls.Length != 0) {
			try {
				foreach (string videoUrl in urls) {
					Video video = await youtube.Videos.GetAsync(videoUrl);
					videos.Add(video);
				}
			}
			catch (Exception e) {
				Console.WriteLine($"Error while decoding videos : {e.Message}");
			}
		}

		uint videoCount = (uint)videos.Count;
		uint trimmedVideoCount = videoCount;
		uint successCount = 0;
		foreach (IVideo video in videos) {
			if (outputDirectory.GetFiles().Any(file => file.Name == $"{GetSanitizedTitle(video)}.{format}")) {
				Console.WriteLine($"Video already exists in the output directory, skipping : {video.Title} ({video.Url})");
				trimmedVideoCount--;
				continue;
			}

			try {
				await DownloadYouTubeVideo(video, outputDirectory, ffmpegDirectory, format);
				successCount++;
			}
			catch (Exception e) {
				Console.WriteLine($"Error occurred while downloading video : {e.Message}");
			}
		}

		Console.WriteLine($"{(successCount == 1 ? "1 video was" : $"{successCount} videos were")} downloaded out of {trimmedVideoCount} ({videos.Count} requested)!");
		if (isConsoleFed) {
			Console.WriteLine("Press any key to exit...");
			Console.ReadKey();
		}
	}
	private static async Task DownloadYouTubeVideo(IVideo video, DirectoryInfo outputDirectory, FileInfo ffmpegFile, string format) {
		YoutubeClient? youtube = new();

		string outputFilePath = Path.Combine(outputDirectory.FullName, $"{GetSanitizedTitle(video)}.{format}");

		Console.WriteLine($"Downloading video : {video.Title} ({video.Url})");

		double progress = 0;

		IProgress<double> progressMonitor = new Progress<double>(p => progress = p);
		ValueTask downloadTask = youtube.Videos.DownloadAsync(video.Url, outputFilePath, o => o
			.SetContainer(format)
			.SetPreset(ConversionPreset.VerySlow)
			.SetFFmpegPath(ffmpegFile.FullName),
			progressMonitor
		);

		char[] spinnerChars = ['|', '/', '-', '\\'];
		int spinnerIndex = 0;
		while (!downloadTask.IsCompleted) {
			const int barWidth = 50; // Width of the progress bar
			int filledBars = (int)Math.Min(Math.Floor(progress * barWidth), barWidth);

			string progressBar = $"[{new string('#', filledBars)}{new string('-', barWidth - filledBars)}] {progress:P0}";
			string spinner = spinnerChars[spinnerIndex].ToString();

			Console.Write($"\r{progressBar} {spinner}");
			spinnerIndex = (spinnerIndex + 1) % spinnerChars.Length;

			await Task.Delay(100);
		}

		await downloadTask;

		Console.Write($"\rDownload completed : {outputFilePath}\n");
	}
}
