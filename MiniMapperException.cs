using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    public class MiniMapperException : Exception
    {
        public MiniMapperException(string message) : base(message) { }
        public MiniMapperException(string message, Exception innerException) : base(message, innerException) { }
    }
}
