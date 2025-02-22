﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shield.Estimator.Business.Exceptions
{
    public class FailedAiRequestException : BusinessException
    {
        public FailedAiRequestException(string message, Exception exception) 
            : base(message, exception)
        {
        }
    }
}
