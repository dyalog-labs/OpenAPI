# Examples

Practical examples of using the OpenAPI Client Generator and generated clients.

## Example 1: PetStore API

### Generating the Client

```bash
cd OpenAPIDyalog
dotnet run -- ../pet-store/openapi.json ../pet-store/PetStore-Client
```

### Using the Generated Client

```apl
⍝ Load the generated client
]LINK.Import # pet-store/PetStore-Client/APLSource

⍝ Create a client instance
client←⎕NEW Client (baseUrl:'https://petstore3.swagger.io/api/v3')

⍝ Find available pets
pets←api.pet.findPetsByStatus.sync (client:client ⋄ status:'available')
⎕←'Available pets: ',≢pets

⍝ Get a specific pet
pet←api.pet.getPetById.sync (client:client ⋄ petId:1)
⎕←'Pet name: ',pet.name

⍝ Create a new pet
newPet←⎕NEW models.Pet
newPet.name←'Fluffy'
newPet.status←'available'
result←api.pet.addPet.syncDetailed (client:client ⋄ Pet:newPet)

:If result.statusCode≡200
    ⎕←'Created pet with ID: ',⍕result.parsed.id
:Else
    ⎕←'Error'
:EndIf
```

## Next Steps

- [API Reference](api-reference.md) - Complete API documentation
- [Contributing](contributing.md) - How to contribute to the project
