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
                .ForMember(d => d.UserId, o => o.MapFrom(s => s.UserId))
                .ForMember(d => d.Role, o => o.MapFrom(s => s.Role != null ? s.Role.RoleName : null));

            CreateMap<User, UserProfileDto>()
                .ForMember(d => d.UserId, o => o.MapFrom(s => s.UserId))
                .ForMember(d => d.Role, o => o.MapFrom(s => s.Role != null ? s.Role.RoleName : null));
        }
    }
}
