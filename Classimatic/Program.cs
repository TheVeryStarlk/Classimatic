using System.CommandLine;
using System.IO.Compression;
using Raspite.Serializer;
using Raspite.Serializer.Tags;

var tokenSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
	eventArgs.Cancel = true;
	tokenSource.Cancel();
};

var pathOption = new Option<string?>(
	[
		"--path",
		"-p"
	],
	"The source path of the Classic schematic file.");

var rootCommand = new RootCommand("A tool for converting Classic schematic files to ClassicWorld files.")
{
	TreatUnmatchedTokensAsErrors = true
};

rootCommand.AddOption(pathOption);

rootCommand.SetHandler(async path =>
{
	if (string.IsNullOrWhiteSpace(path))
	{
		Console.ForegroundColor = ConsoleColor.Red;
		Console.Error.WriteLine("Please specify a valid path.");
		Console.ResetColor();

		return;
	}

	try
	{
		await using var source = File.OpenRead(path);
		await using var decompressor = new GZipStream(source, CompressionMode.Decompress);

		var name = Path.GetFileNameWithoutExtension(path);
		Console.WriteLine($"Started converting '{name}'...");

		var schematic = await BinaryTagSerializer.DeserializeAsync<CompoundTag>(
			decompressor,
			new BinaryTagSerializerOptions
			{
				MinimumLength = int.MaxValue
			},
			tokenSource.Token);

		var width = schematic.First<ShortTag>("Width").Value;
		var height = schematic.First<ShortTag>("Height").Value;
		var length = schematic.First<ShortTag>("Length").Value;

		var spawn = CompoundTag.Create("Spawn")
			.Add(ShortTag.Create((short) (width / 2), "X"))
			.Add(ShortTag.Create((short) (height / 2), "Y"))
			.Add(ShortTag.Create((short) (length / 2), "Z"))
			.Add(ByteTag.Create(0, "H"))
			.Add(ByteTag.Create(0, "P"))
			.Build();

		var classicWorld = CompoundTag.Create("ClassicWorld")
			.Add(ByteTag.Create(1, "FormatVersion"))
			.Add(StringTag.Create("Name", name))
			.Add(ByteCollectionTag.Create(Guid.NewGuid().ToByteArray(), "UUID"))
			.Add(ShortTag.Create(width, "X"))
			.Add(ShortTag.Create(height, "Y"))
			.Add(ShortTag.Create(length, "Z"))
			.Add(spawn)
			.Add(ByteCollectionTag.Create(schematic.First<ByteCollectionTag>("Blocks").Children, "BlockArray"))
			.Build();

		var result = Path.Combine(Directory.GetParent(path)!.FullName, $"{name}.cw");

		await using var destination = File.OpenWrite(result);
		await using var compressor = new GZipStream(destination, CompressionMode.Compress);

		await BinaryTagSerializer.SerializeAsync(classicWorld, compressor, cancellationToken: tokenSource.Token);

		Console.WriteLine();

		Console.ForegroundColor = ConsoleColor.DarkGreen;
		Console.WriteLine("Finished converting. File has been saved at:");
		Console.WriteLine(result);
		Console.ResetColor();
	}
	catch (Exception exception)
	{
		Console.WriteLine();

		Console.ForegroundColor = ConsoleColor.Red;
		Console.Error.WriteLine(exception.Message);
		Console.ResetColor();
	}
}, pathOption);

Environment.Exit(await rootCommand.InvokeAsync(args));