using AutoMapper;
using ColdChainX.Application.DTOs;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<User, AuthResponseDto>()
                .ForMember(d => d.UserId, o => o.MapFrom(s => s.Id));

            CreateMap<User, UserProfileDto>()
                .ForMember(d => d.UserId, o => o.MapFrom(s => s.Id));
        }
    }
}
