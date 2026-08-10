using System;
using System.IO;

namespace VoxelCubemapApi.Server.PlanetModification.Persistence
{
    internal static class BinaryData
    {
        internal static byte[] ReadAll(
            BinaryReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException("reader");

            byte[] output =
                new byte[64 * 1024];

            int length =
                0;

            while (true)
            {
                if (length == output.Length)
                {
                    byte[] grown =
                        new byte[output.Length * 2];

                    Buffer.BlockCopy(
                        output,
                        0,
                        grown,
                        0,
                        output.Length);

                    output =
                        grown;
                }

                int read =
                    reader.Read(
                        output,
                        length,
                        output.Length - length);

                if (read <= 0)
                    break;

                length +=
                    read;
            }

            if (length == output.Length)
                return output;

            byte[] exact =
                new byte[length];

            if (length > 0)
            {
                Buffer.BlockCopy(
                    output,
                    0,
                    exact,
                    0,
                    length);
            }

            return exact;
        }
    }
}
