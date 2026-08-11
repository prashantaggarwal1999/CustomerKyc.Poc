using AutoMapper;
using CustomerKyc.Api.DTOs;
using CustomerKyc.Api.Encryption;
using CustomerKyc.Api.Models;
using CustomerKyc.Api.Repositories;

namespace CustomerKyc.Api.Services;

public sealed class CustomerKycService : ICustomerKycService
{
    private readonly ICustomerKycRepository _repository;
    private readonly ITdesEncryptionService _encryption;
    private readonly IMapper _mapper;
    private readonly ILogger<CustomerKycService> _logger;

    public CustomerKycService(
        ICustomerKycRepository repository,
        ITdesEncryptionService encryption,
        IMapper mapper,
        ILogger<CustomerKycService> logger)
    {
        _repository = repository;
        _encryption = encryption;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<CreateCustomerResponse> CreateAsync(CreateCustomerKycRequest request)
    {
        _logger.LogInformation("Creating KYC record for {Ref}", request.CustomerReference);

        // Encrypt sensitive fields via TDESEncrypt.dll before persisting.
        var encryptedPan = _encryption.Encrypt(request.Pan);
        var encryptedAadhaar = _encryption.Encrypt(request.Aadhaar);

        var entity = new CustomerKycEntity
        {
            CustomerReference = request.CustomerReference,
            FirstName = request.FirstName,
            LastName = request.LastName,
            EncryptedPan = encryptedPan,
            EncryptedAadhaar = encryptedAadhaar,
            Status = "Active",
            CreatedOn = DateTime.UtcNow
        };

        var id = await _repository.InsertAsync(entity);

        _logger.LogInformation("KYC record created. Id={Id}", id);

        return new CreateCustomerResponse
        {
            Id = id,
            CustomerReference = request.CustomerReference,
            Status = entity.Status
        };
    }

    public async Task<CustomerKycResponse?> GetByIdAsync(long id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null) return null;

        // AutoMapper handles all non-sensitive fields.
        var response = _mapper.Map<CustomerKycResponse>(entity);

        // Decrypt explicitly — encryption/decryption is never delegated to AutoMapper.
        response.Pan = _encryption.Decrypt(entity.EncryptedPan);
        response.Aadhaar = _encryption.Decrypt(entity.EncryptedAadhaar);

        return response;
    }
}
