using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shield.Estimator.Business.Exceptions
{
    public class FailedLoggerToFileException : BusinessException
    {
        public FailedLoggerToFileException(string message, Exception? exception = null)  
            : base(message, exception)
        {
        }
    }
}
