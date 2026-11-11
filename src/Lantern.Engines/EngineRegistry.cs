// THE place where conversion engines are registered. Adding an engine = one line in CreateAll.

using Lantern.Core.Conversion;
using Lantern.Engines.Archives;
using Lantern.Engines.Cad;
using Lantern.Engines.Data;
using Lantern.Engines.Ebooks;
using Lantern.Engines.Email;
using Lantern.Engines.Fonts;
using Lantern.Engines.Geo;
using Lantern.Engines.Hwp;
using Lantern.Engines.Hwp.Hwpx;
using Lantern.Engines.Icns;
using Lantern.Engines.Infrastructure;
using Lantern.Engines.Magick;
using Lantern.Engines.Media;
using Lantern.Engines.Metadata;
using Lantern.Engines.Models3D;
using Lantern.Engines.Ocr;
using Lantern.Engines.Office;
using Lantern.Engines.Pdf;
using Lantern.Engines.Signed;
using Lantern.Engines.Subtitles;
using Lantern.Engines.Text;
using Lantern.Engines.Vector;
using Lantern.Engines.Wic;
using Microsoft.Extensions.Logging;

namespace Lantern.Engines;

/// <summary>Creates every built-in converter.</summary>
public static class EngineRegistry
{
    /// <summary>
    /// Instantiates all converters. Order only matters as a tie-breaker when no priority is set, so it mirrors
    /// AppSettings.EnginePriority: built-in engines first, the optional downloads last. Software installed on the
    /// system (Microsoft Office, a system-wide LibreOffice) is never used.
    /// </summary>
    /// <param name="loggers">
    /// The app's log; FFmpeg writes which hardware encoders work and when one failed, LibreOffice when a file needed
    /// its container repacked.
    /// </param>
    public static IReadOnlyList<IConverter> CreateAll(EngineEnvironment env, ILoggerFactory? loggers = null) =>
    [
        new MagickImageConverter(),
        new FolderIsoConverter(),
        new PdfSharpConverter(),
        new PdfiumConverter(),
        new RhwpConverter(),
        new UnhwpConverter(),
        new MarkdownConverter(),
        new SpreadsheetConverter(),
        new OoxmlConverter(),
        new HwpxConverter(),
        new Translation.TranslatorConverter(loggers?.CreateLogger<Translation.TranslatorConverter>()),
        new ArchiveConverter(),
        new VectorConverter(),
        new IcnsConverter(),
        new FontConverter(),
        new DocxWriterConverter(),
        new PdfTextConverter(),
        new PdfSlidesConverter(),
        new PdfTablesConverter(),
        new DocumentTablesConverter(),
        new EmlConverter(),
        new P7mConverter(),
        new DataConverter(),
        new SubtitleConverter(),
        new WicImageConverter(),
        new WhisperConverter(loggers?.CreateLogger<WhisperConverter>()),
        new ExifToolConverter(),
        new Upscale.UpscalerConverter(),
        new FfmpegConverter(loggers?.CreateLogger<FfmpegConverter>()),
        new CadConverter(),
        new ModelConverter(),
        new ImageTo3DConverter(),
        new GeoConverter(),
        new EbookConverter(),
        new PandocConverter(),
        new GhostscriptConverter(),
        new VisionConverter(), // before Tesseract: a Mac's OCR step (Tesseract reads the languages Vision doesn't)
        new TesseractConverter(),
        new CalibreConverter(),
        new LibreOfficeConverter(env, loggers?.CreateLogger<LibreOfficeConverter>()),
    ];

    /// <summary>Finds the PDF merger among the converters.</summary>
    public static IPdfMerger? FindPdfMerger(IEnumerable<IConverter> converters) =>
        converters.OfType<IPdfMerger>().FirstOrDefault();

    /// <summary>Finds the engine that writes several pages into one multi-page TIFF.</summary>
    public static ITiffMerger? FindTiffMerger(IEnumerable<IConverter> converters) =>
        converters.OfType<ITiffMerger>().FirstOrDefault();

    /// <summary>Finds the engine that packs several files into one archive ("Compress into one archive").</summary>
    public static IFileCombiner? FindFileCombiner(IEnumerable<IConverter> converters) =>
        converters.OfType<IFileCombiner>().FirstOrDefault();

    /// <summary>The engine that joins videos, audio files and pictures into one (FFmpeg).</summary>
    public static IMediaJoiner? FindMediaJoiner(IEnumerable<IConverter> converters) =>
        converters.OfType<IMediaJoiner>().FirstOrDefault();
}
