namespace Fogos.Infrastructure.Images;

/// <summary>Base for every rejection the image pipeline raises; each maps to a specific HTTP status.</summary>
public abstract class ImageProcessingException(string message) : Exception(message);

/// <summary>The upload is neither JPEG nor PNG (→ HTTP 415 Unsupported Media Type).</summary>
public sealed class UnsupportedImageFormatException(string message = "Only JPEG and PNG uploads are accepted.")
    : ImageProcessingException(message);

/// <summary>The bytes claim to be JPEG/PNG but could not be decoded (→ HTTP 400 Bad Request).</summary>
public sealed class UndecodableImageException(string message = "The image could not be decoded.")
    : ImageProcessingException(message);

/// <summary>No usable GPS EXIF was found (→ HTTP 422 Unprocessable Entity). GPS is required on upload.</summary>
public sealed class MissingGpsException(string message = "The photo has no GPS EXIF coordinates.")
    : ImageProcessingException(message);
