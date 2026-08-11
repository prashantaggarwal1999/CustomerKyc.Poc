using AutoMapper;
using CustomerKyc.Api.DTOs;
using CustomerKyc.Api.Models;

namespace CustomerKyc.Api.Mapping;

public sealed class CustomerKycProfile : Profile
{
    public CustomerKycProfile()
    {
        // Map DB entity → API response DTO.
        // NOTE: Encrypted fields (EncryptedPan / EncryptedAadhaar) are intentionally
        // NOT mapped here. Decryption is an explicit, auditable step in the service layer.
        CreateMap<CustomerKycEntity, CustomerKycResponse>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
            .ForMember(d => d.CustomerReference, o => o.MapFrom(s => s.CustomerReference))
            .ForMember(d => d.FirstName, o => o.MapFrom(s => s.FirstName))
            .ForMember(d => d.LastName, o => o.MapFrom(s => s.LastName))
            .ForMember(d => d.Status, o => o.MapFrom(s => s.Status))
            .ForMember(d => d.CreatedOn, o => o.MapFrom(s => s.CreatedOn))
            .ForMember(d => d.Pan, o => o.Ignore())       // set explicitly after decryption
            .ForMember(d => d.Aadhaar, o => o.Ignore());  // set explicitly after decryption
    }
}
