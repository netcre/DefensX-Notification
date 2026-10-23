using System.Drawing;
using System.Drawing.Imaging;

namespace DwhMonitor.App;

internal sealed class CaptchaPrimaryModel
{
    private const int Width = 240;
    private const int Height = 80;
    private const int CropLeftMargin = 12;
    private const int CropWidth = 44;
    private const int DigitCount = 6;
    private const int CategoryCount = 11;
    private const int AlignmentIndex = 11;
    private const float AlignmentWeight = 1f;
    private readonly float[] _conv1Weight;
    private readonly float[] _conv1Bias;
    private readonly float[] _conv2Weight;
    private readonly float[] _conv2Bias;
    private readonly float[] _conv3Weight;
    private readonly float[] _conv3Bias;
    private readonly float[] _fc1Weight;
    private readonly float[] _fc1Bias;
    private readonly float[] _fc2Weight;
    private readonly float[] _fc2Bias;

    public CaptchaPrimaryModel(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadBytes(9) is not [(byte)'D', (byte)'W', (byte)'C', (byte)'M', (byte)'O', (byte)'D', (byte)'E', (byte)'L', (byte)'1'])
        {
            throw new InvalidDataException("验证码模型文件头不正确。");
        }

        var tensors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var tensorCount = reader.ReadInt32();
        for (var tensorIndex = 0; tensorIndex < tensorCount; tensorIndex++)
        {
            var name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
            var rank = reader.ReadInt32();
            var length = 1;
            for (var dimension = 0; dimension < rank; dimension++)
            {
                length = checked(length * reader.ReadInt32());
            }

            var bytes = reader.ReadBytes(checked(length * sizeof(float)));
            if (bytes.Length != length * sizeof(float))
            {
                throw new InvalidDataException($"验证码模型张量 {name} 数据不完整。");
            }

            var values = new float[length];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            tensors.Add(name, values);
        }

        _conv1Weight = Take(tensors, "features.0.weight", 16 * 2 * 3 * 3);
        _conv1Bias = Take(tensors, "features.0.bias", 16);
        _conv2Weight = Take(tensors, "features.3.weight", 32 * 16 * 3 * 3);
        _conv2Bias = Take(tensors, "features.3.bias", 32);
        _conv3Weight = Take(tensors, "features.6.weight", 64 * 32 * 3 * 3);
        _conv3Bias = Take(tensors, "features.6.bias", 64);
        _fc1Weight = Take(tensors, "classifier.1.weight", 128 * (64 * 4 * 4));
        _fc1Bias = Take(tensors, "classifier.1.bias", 128);
        _fc2Weight = Take(tensors, "classifier.3.weight", 12 * 128);
        _fc2Bias = Take(tensors, "classifier.3.bias", 12);
    }

    public string? Recognize(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes, writable: false);
        using var bitmap = new Bitmap(stream);
        if (bitmap.Width != Width || bitmap.Height != Height)
        {
            return null;
        }

        var foreground = FindPrimaryColor(bitmap);
        if (foreground is null)
        {
            return null;
        }

        var input = new float[Height * Width];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                input[y * Width + x] = color.A != 0 && color.ToArgb() == foreground.Value.ToArgb() ? 1f : 0f;
            }
        }

        var opened = RemoveThinLines(input);
        var candidates = new Dictionary<int, CandidateResult>();
        for (var startX = 16; startX <= 65; startX += 5)
        {
            candidates[startX] = EvaluateCandidate(input, opened, startX);
        }

        var coarseWinner = candidates.MaxBy(pair => pair.Value.Score).Key;
        for (var startX = Math.Max(16, coarseWinner - 4); startX <= Math.Min(65, coarseWinner + 4); startX++)
        {
            if (!candidates.ContainsKey(startX))
            {
                candidates[startX] = EvaluateCandidate(input, opened, startX);
            }
        }

        var winner = candidates.MaxBy(pair => pair.Value.Score).Value;
        return winner.Confidence >= 0.75f ? winner.Code : null;
    }

    private CandidateResult EvaluateCandidate(float[] input, float[] opened, int startX)
    {
        var code = new char[DigitCount];
        var score = 0f;
        var confidence = 0f;
        for (var position = 0; position < DigitCount; position++)
        {
            var left = startX + position * 26 - CropLeftMargin;
            var crop = CropChannels(input, opened, left);
            var logits = Predict(crop);
            var maximum = Enumerable.Range(0, CategoryCount).Max(index => logits[index]);
            var sum = 0f;
            var winner = 0;
            var winnerProbability = 0f;
            for (var category = 0; category < CategoryCount; category++)
            {
                var probability = MathF.Exp(logits[category] - maximum);
                sum += probability;
                if (category < 10 && probability > winnerProbability)
                {
                    winnerProbability = probability;
                    winner = category;
                }
            }

            code[position] = (char)('0' + winner);
            var digitProbability = winnerProbability / sum;
            confidence += digitProbability;
            score += MathF.Log(MathF.Max(digitProbability, 1e-8f));
            score += AlignmentWeight * MathF.Log(MathF.Max(Sigmoid(logits[AlignmentIndex]), 1e-8f));
        }

        return new CandidateResult(new string(code), score, confidence / DigitCount);
    }

    private static float[] CropChannels(float[] raw, float[] opened, int left)
    {
        var crop = new float[2 * Height * CropWidth];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < CropWidth; x++)
            {
                var sourceX = left + x;
                if (sourceX < 0 || sourceX >= Width)
                {
                    continue;
                }

                crop[y * CropWidth + x] = raw[y * Width + sourceX];
                crop[Height * CropWidth + y * CropWidth + x] = opened[y * Width + sourceX];
            }
        }

        return crop;
    }

    private static float[] RemoveThinLines(float[] source)
    {
        var eroded = new float[Height * Width];
        for (var y = 1; y < Height - 1; y++)
        {
            for (var x = 1; x < Width - 1; x++)
            {
                var index = y * Width + x;
                eroded[index] = source[index] > 0f &&
                    source[index - Width] > 0f &&
                    source[index + Width] > 0f &&
                    source[index - 1] > 0f &&
                    source[index + 1] > 0f
                    ? 1f
                    : 0f;
            }
        }

        var opened = (float[])eroded.Clone();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var index = y * Width + x;
                if (eroded[index] > 0f ||
                    y > 0 && eroded[index - Width] > 0f ||
                    y + 1 < Height && eroded[index + Width] > 0f ||
                    x > 0 && eroded[index - 1] > 0f ||
                    x + 1 < Width && eroded[index + 1] > 0f)
                {
                    opened[index] = 1f;
                }
            }
        }

        return opened;
    }

    private float[] Predict(float[] input)
    {
        var first = Relu(Convolve(input, 2, Height, CropWidth, _conv1Weight, _conv1Bias, 16));
        var firstPooled = MaxPool(first, 16, Height, CropWidth);
        var second = Relu(Convolve(firstPooled, 16, Height / 2, CropWidth / 2, _conv2Weight, _conv2Bias, 32));
        var secondPooled = MaxPool(second, 32, Height / 2, CropWidth / 2);
        var third = Relu(Convolve(secondPooled, 32, Height / 4, CropWidth / 4, _conv3Weight, _conv3Bias, 64));
        var adaptive = AdaptiveMaxPool(third, 64, Height / 4, CropWidth / 4, 4, 4);
        var hidden = Relu(Dense(adaptive, _fc1Weight, _fc1Bias, 128));
        return Dense(hidden, _fc2Weight, _fc2Bias, 12);
    }

    private static float Sigmoid(float value) => 1f / (1f + MathF.Exp(-value));

    private sealed record CandidateResult(string Code, float Score, float Confidence);

    private static Color? FindPrimaryColor(Bitmap bitmap)
    {
        var colors = new Dictionary<int, int>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0 || color.R > 245 && color.G > 245 && color.B > 245)
                {
                    continue;
                }

                colors.TryGetValue(color.ToArgb(), out var count);
                colors[color.ToArgb()] = count + 1;
            }
        }

        var winner = colors.OrderByDescending(pair => pair.Value).FirstOrDefault();
        return winner.Value == 0 ? null : Color.FromArgb(winner.Key);
    }

    private static float[] Convolve(float[] input, int inputChannels, int height, int width, float[] weights, float[] bias, int outputChannels)
    {
        var output = new float[outputChannels * height * width];
        for (var channel = 0; channel < outputChannels; channel++)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sum = bias[channel];
                    for (var inputChannel = 0; inputChannel < inputChannels; inputChannel++)
                    {
                        for (var ky = -1; ky <= 1; ky++)
                        {
                            var sourceY = y + ky;
                            if (sourceY < 0 || sourceY >= height)
                            {
                                continue;
                            }

                            for (var kx = -1; kx <= 1; kx++)
                            {
                                var sourceX = x + kx;
                                if (sourceX < 0 || sourceX >= width)
                                {
                                    continue;
                                }

                                var weightIndex = ((channel * inputChannels + inputChannel) * 3 + (ky + 1)) * 3 + kx + 1;
                                sum += input[(inputChannel * height + sourceY) * width + sourceX] * weights[weightIndex];
                            }
                        }
                    }

                    output[(channel * height + y) * width + x] = sum;
                }
            }
        }

        return output;
    }

    private static float[] MaxPool(float[] input, int channels, int height, int width)
    {
        var output = new float[channels * (height / 2) * (width / 2)];
        var outputHeight = height / 2;
        var outputWidth = width / 2;
        for (var channel = 0; channel < channels; channel++)
        {
            for (var y = 0; y < outputHeight; y++)
            {
                for (var x = 0; x < outputWidth; x++)
                {
                    var maximum = float.NegativeInfinity;
                    for (var ky = 0; ky < 2; ky++)
                    {
                        for (var kx = 0; kx < 2; kx++)
                        {
                            maximum = MathF.Max(maximum, input[(channel * height + y * 2 + ky) * width + x * 2 + kx]);
                        }
                    }

                    output[(channel * outputHeight + y) * outputWidth + x] = maximum;
                }
            }
        }

        return output;
    }

    private static float[] AdaptiveMaxPool(float[] input, int channels, int height, int width, int outputHeight, int outputWidth)
    {
        var output = new float[channels * outputHeight * outputWidth];
        for (var channel = 0; channel < channels; channel++)
        {
            for (var y = 0; y < outputHeight; y++)
            {
                for (var x = 0; x < outputWidth; x++)
                {
                    var maximum = float.NegativeInfinity;
                    var top = y * height / outputHeight;
                    var bottom = ((y + 1) * height + outputHeight - 1) / outputHeight;
                    var left = x * width / outputWidth;
                    var right = ((x + 1) * width + outputWidth - 1) / outputWidth;
                    for (var sourceY = top; sourceY < bottom; sourceY++)
                    {
                        for (var sourceX = left; sourceX < right; sourceX++)
                        {
                            maximum = MathF.Max(maximum, input[(channel * height + sourceY) * width + sourceX]);
                        }
                    }

                    output[(channel * outputHeight + y) * outputWidth + x] = maximum;
                }
            }
        }

        return output;
    }

    private static float[] Dense(float[] input, float[] weights, float[] bias, int outputSize)
    {
        var output = new float[outputSize];
        for (var row = 0; row < outputSize; row++)
        {
            var sum = bias[row];
            for (var column = 0; column < input.Length; column++)
            {
                sum += input[column] * weights[row * input.Length + column];
            }

            output[row] = sum;
        }

        return output;
    }

    private static float[] Relu(float[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = MathF.Max(0f, values[index]);
        }

        return values;
    }

    private static float[] Take(IReadOnlyDictionary<string, float[]> tensors, string name, int expectedLength)
    {
        if (!tensors.TryGetValue(name, out var values) || values.Length != expectedLength)
        {
            throw new InvalidDataException($"验证码模型张量 {name} 尺寸不匹配。");
        }

        return values;
    }
}
