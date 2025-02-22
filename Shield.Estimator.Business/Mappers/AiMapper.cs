namespace Shield.Estimator.Business.Mappers;

using AutoMapper;
using Shield.Estimator.Business.Models.KoboldDto;
using Shield.Estimator.Business.Options.KoboldOptions;

public class AiMapper : Profile
{
    public AiMapper()
    {
        CreateMap<AiPromptOptions, AiRequestDto>();
    }
}
