using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shield.Estimator.Business.Services.WhisperNet
{
    public static class BufferConverter
    {
        public static short[] ConvertToShorts(byte[] buffer)
        {
            var result = new short[buffer.Length / 2];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = BitConverter.ToInt16(buffer, i * 2);
            }
            return result;
        }
    }
}
