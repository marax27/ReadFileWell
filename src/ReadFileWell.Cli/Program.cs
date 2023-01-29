using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Text;

// Goal: Find a shortest movie title and a longest movie title in a file.

var b = new FileReadBenchmark();
//Console.WriteLine($" [0]>> {b.ReadAllSplitLinesCase()}");
//Console.WriteLine($" [1]>> {b.ReadLineByLineCase()}");
Console.WriteLine($" [2]>> {b.ReadSmart()}");
// BenchmarkRunner.Run<FileReadBenchmark>();


public static class ProgramInput
{
    public const string Path = "c:/workspace/_csharp/ReadFileWell/data.tsv";
}


[ShortRunJob]
[MemoryDiagnoser]
public class FileReadBenchmark
{
    [Benchmark]
    public RunResult ReadAllSplitLinesCase()
    {
        var content = File.ReadAllText(ProgramInput.Path);

        var lines = content.Split('\n');
        (string longest, string shortest) = (null, null);

        for (int i=1; i < lines.Length; ++i)
        {
            var pieces = lines[i].Split('\t', 4);
            if (pieces.Length < 2) continue;

            var titlePiece = pieces[2];
            longest = longest is null ? titlePiece : (titlePiece.Length > longest.Length ? titlePiece : longest);
            shortest = shortest is null ? titlePiece : (titlePiece.Length < shortest.Length ? titlePiece : shortest);
        }

        return new(shortest, longest);
    }

    [Benchmark]
    public RunResult ReadLineByLineCase()
    {
        string shortest = null;
        string longest = null;
        var isFirst = true;

        foreach (var line in File.ReadLines(ProgramInput.Path))
        {

            if (isFirst)
            {
                isFirst = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var pieces = line.Split('\t');
            var titlePiece = pieces[2];

            longest = longest is null ? titlePiece : (titlePiece.Length > longest.Length ? titlePiece : longest);
            shortest = shortest is null ? titlePiece : (titlePiece.Length < shortest.Length ? titlePiece : shortest);
        }

        return new(shortest, longest);
    }

    [Benchmark]
    public RunResult ReadSmart()
    {
        ReadOnlySpan<byte> tSep = stackalloc byte[] { 9 };
        ReadOnlySpan<byte> nSep = stackalloc byte[] { 10 };

        Span<byte> shortest = stackalloc byte[512];
        int shortestLength = -1;

        Span<byte> longest = stackalloc byte[512];
        int longestLength = -1;

        Span<byte> fileReadBuffer = stackalloc byte[1024];
        var fileReadCursor = 0;

        using var fs = new FileStream(ProgramInput.Path, FileMode.Open, FileAccess.Read);

        /* 0: find \t, skip, ->1
            1: find \t, skip, ->2
            2: read all, ->3
            3: find \n, skip, ->0
        */
        int state = 0;
        void UpdateState() => state = (state + 1) % 9;

        Span<byte> primaryTitle = stackalloc byte[1024];
        var titleLoadedBytes = 0;
        int n = 0;
        bool loadMore = true;

        while (true)
        {
            if (loadMore)
            {
                n = fs.Read(fileReadBuffer);
                fileReadCursor = 0;
                loadMore = false;
                if (n == 0)
                    break;
            }

            if (state == 0 || state == 1)
            {
                var nextSeparatorPosRelative = fileReadBuffer[fileReadCursor..n].IndexOf(tSep);
                loadMore = nextSeparatorPosRelative == -1;

                var nextSeparatorPos = nextSeparatorPosRelative + fileReadCursor;

                if (nextSeparatorPos != -1)
                {
                    fileReadCursor= nextSeparatorPos + 1;
                    ++state;
                }
            }
            else if (state == 2)
            {
                var nextSeparatorPosRelative = fileReadBuffer[fileReadCursor..n].IndexOf(tSep);
                loadMore = nextSeparatorPosRelative == -1;

                var nextSeparatorPos = nextSeparatorPosRelative + fileReadCursor;

                if (loadMore)
                {
                    fileReadBuffer[fileReadCursor..n].CopyTo(primaryTitle[titleLoadedBytes..]);
                    titleLoadedBytes += n - fileReadCursor;
                }
                else
                {
                    // copy to title buffer.
                    fileReadBuffer[fileReadCursor..nextSeparatorPos].CopyTo(primaryTitle[titleLoadedBytes..]);
                    titleLoadedBytes += nextSeparatorPos - fileReadCursor;
                    var primaryTitleSpan = primaryTitle[..titleLoadedBytes];

                    // evaluate shortest
                    if (shortestLength == -1 || titleLoadedBytes < shortestLength)
                    {
                        primaryTitleSpan.CopyTo(shortest);
                        shortestLength = titleLoadedBytes;
                    }

                    // evaluate longest
                    if (longestLength == -1 || titleLoadedBytes > longestLength)
                    {
                        primaryTitleSpan.CopyTo(longest);
                        longestLength = titleLoadedBytes;
                    }

                    // clear title bytes count.
                    titleLoadedBytes = 0;

                    fileReadCursor = nextSeparatorPos + 1;
                    ++state;
                }
            }
            else if (state == 3)
            {
                var nextSeparatorPosRelative = fileReadBuffer[fileReadCursor..n].IndexOf(nSep);
                loadMore = nextSeparatorPosRelative == -1;

                var nextSeparatorPos = nextSeparatorPosRelative + fileReadCursor;

                if (nextSeparatorPos != -1)
                {
                    fileReadCursor = nextSeparatorPos + 1;
                    state = 0;
                }
            }
        }

        return new(
            Encoding.UTF8.GetString(shortest[..shortestLength].ToArray()),
            Encoding.UTF8.GetString(longest[..longestLength].ToArray())
        );
    }
}

public record RunResult(
    string ShortestTitle,
    string LongestTitle
);
