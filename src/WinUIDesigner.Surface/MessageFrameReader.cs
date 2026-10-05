// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.IO;

namespace WinUIDesigner.Surface;

internal static class MessageFrameReader
{
    // Includes the three remaining header integers, but excludes the length prefix.
    internal const int MaximumLength = 64 * 1024 * 1024;

    public static byte[]? Read(Func<byte[], int, int, int> read)
    {
        byte[] prefix = new byte[4];
        int first = read(prefix, 0, prefix.Length);
        if (first == 0)
        {
            return null;
        }

        Fill(read, prefix, first);
        int length = BitConverter.ToInt32(prefix);
        if (length < 12 || length > MaximumLength)
        {
            throw new InvalidDataException($"Invalid designer message length: {length}.");
        }

        byte[] frame = new byte[checked(length + 4)];
        prefix.CopyTo(frame, 0);
        Fill(read, frame, 4);

        return frame;
    }

    private static void Fill(Func<byte[], int, int, int> read, byte[] buffer, int offset)
    {
        while (offset < buffer.Length)
        {
            int count = read(buffer, offset, buffer.Length - offset);
            if (count <= 0)
            {
                throw new EndOfStreamException("The designer pipe closed in the middle of a message.");
            }

            offset += count;
        }
    }
}
