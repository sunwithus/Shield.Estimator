using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shield.Estimator.Business.Models.KoboldDto;

public class AiResponseDto
{
    public List<AiResultResponseDto> Results { get; set; }
}
