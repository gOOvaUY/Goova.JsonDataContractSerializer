using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Goova.JsonDataContractSerializer
{
    public class StreamWithHeaders : Stream
    {
        public Stream Stream { get; set; }
        public Dictionary<string, string> Headers { get; set; }

        public bool HasContent => Stream != null;

        public StreamWithHeaders(Stream stream)
        {
            Stream = stream;
        }


        public override void Flush()
        {
            Stream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return Stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            Stream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Stream.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Stream.Write(buffer, offset, count);
        }

        public override bool CanRead => Stream.CanRead;
        public override bool CanSeek => Stream.CanSeek;
        public override bool CanWrite => Stream.CanWrite;
        public override long Length => Stream.Length;

        public override long Position
        {
            get { return Stream.Position; }
            set { Stream.Position = value; }
        }

    }
}
