using WeddingGallery.Application.Media;
using WeddingGallery.Domain;

namespace WeddingGallery.Application.Tests.Media;

public class MediaFileValidatorTests
{
    private readonly MediaFileValidator _validator = new();

    [Theory]
    [InlineData("kiss.jpg")]
    [InlineData("kiss.jpeg")]
    [InlineData("kiss.png")]
    [InlineData("kiss.webp")]
    [InlineData("IMG_0042.HEIC")]
    [InlineData("confetti.gif")]
    public void Recognises_image_extensions(string fileName)
    {
        var result = _validator.Validate(fileName, 2 * 1024 * 1024);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(MediaTypes.Image, result.MediaType);
    }

    [Theory]
    [InlineData("first-dance.mp4")]
    [InlineData("speech.MOV")]
    [InlineData("toast.m4v")]
    [InlineData("cake.webm")]
    public void Recognises_video_extensions(string fileName)
    {
        var result = _validator.Validate(fileName, 20 * 1024 * 1024);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(MediaTypes.Video, result.MediaType);
    }

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("shell.php")]
    [InlineData("no-extension")]
    public void Rejects_everything_outside_the_allowlist(string fileName)
    {
        var result = _validator.Validate(fileName, 1024);

        Assert.False(result.IsValid);
        Assert.Null(result.MediaType);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Rejects_files_over_the_size_cap()
    {
        var result = _validator.Validate("long-speech.mp4", MediaFileValidator.MaxFileBytes + 1);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Accepts_a_file_exactly_at_the_size_cap()
    {
        var result = _validator.Validate("long-speech.mp4", MediaFileValidator.MaxFileBytes);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(MediaTypes.Video, result.MediaType);
    }

    [Fact]
    public void Rejects_empty_files()
    {
        var result = _validator.Validate("kiss.jpg", 0);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Judges_the_extension_only_so_a_traversal_name_still_classifies()
    {
        // Path sanitization is PhotoService's job (Path.GetFileName); the validator
        // must not silently accept-or-reject based on directory noise in the name.
        var result = _validator.Validate("../../../etc/passwd.jpg", 1024);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(MediaTypes.Image, result.MediaType);
    }

    [Fact]
    public void Error_message_names_the_offending_file()
    {
        var result = _validator.Validate("payload.exe", 1024);

        Assert.Contains("payload.exe", result.Error);
    }
}
