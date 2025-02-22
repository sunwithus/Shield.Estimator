using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shield.Estimator.Business.Exceptions
{
    public class BusinessException : Exception
    {
        public BusinessException(string? message, Exception? innerException) 
            : base(message, innerException)
        {
        }
    }
}
