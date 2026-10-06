# Models

Each object schema in the spec's `components` produces a class in `APLSource/models/`. A request body defined within an operation, rather than by reference to a schema, also produces a class, named after the operation: `<OperationId>Request`.

```
APLSource/models/<Model>.aplc
```

A schema that is not an object — an array or a string, say — produces no class. Wherever it is used, its value is used directly: a schema that is an array of `Pet`s is used as a vector of `models.Pet` instances (or namespaces).

A schema that extends others with `allOf` has their properties too, to any depth.

Models are optional. A request body can always be given as a plain namespace, and a response is always available as a namespace in `response.Data`. Models add checking of required fields and enum values when building a request body, and a typed object to read a response into.

## Creating an instance

Pass a namespace of property values to the constructor:

```apl
pet ← ⎕NEW models.Pet (name: 'Rex' ⋄ status: 'available')
```

If a property that the schema marks as required is missing, the constructor signals an error:

```apl
      ⎕NEW models.Pet (status: 'available')
DOMAIN ERROR: Missing required fields: name
```

An instance can also be created empty, with `⎕NEW models.Pet`, and its properties set afterwards.

## Properties

Each property in the schema becomes a property of the class, with the same name. A name that is not valid APL is mangled in the same way as for [parameters](operations.md#passing-parameters), and `⎕JSON` restores the original name when the model is sent.

A property that has not been set reads as `⊂'null'`. A property the schema marks `readOnly` is set by the server: it can be read from a model built with [`FromResponse`](#fromresponse), but cannot be set, and is not sent in a request.

The comment at the top of each class file lists its properties, with their types and whether each is required.

### Enums

Setting a property that the schema restricts to a list of values to any other value signals an error:

```apl
      pet.status ← 'lost'
DOMAIN ERROR: status must be one of: available, pending, sold
```

The allowed values are also available as constants, in a namespace named `Enum<Property>`:

```apl
pet.status ← models.Pet.EnumStatus.Sold    ⍝ 'sold'
```

String values are character vectors, numeric values are numbers, and boolean values are `⊂'true'` and `⊂'false'`, as `⎕JSON` represents them. If the schema allows null, the property can also be set to `⊂'null'`.

## Sending a model

An operation that takes a JSON request body accepts a model instance in place of a namespace (or, for an array body, a vector of them):

```apl
response ← client.pet.AddPet (pet: pet)
```

The instance is converted to a namespace with its `FormatNS` method. This can also be called directly, to see what will be sent:

```apl
      1 ⎕JSON pet.FormatNS
{"name":"Rex","status":"available"}
```

`FormatNS` leaves out properties that have not been set and read-only properties. A property that holds another model, or a vector of them, is converted in turn; it can equally hold a plain namespace.

## FromResponse

`FromResponse` fills an instance from a namespace, such as a parsed JSON response, and returns the instance. Properties that hold other models are filled with instances of those models:

```apl
response ← client.pet.GetPetById (petId: 1)
pet ← (⎕NEW models.Pet).FromResponse response.Data
pet.category.name    ⍝ pet.category is a models.Category
```

## Map types

A schema with no properties of its own describes an object with arbitrary keys, unless it sets `additionalProperties: false`. This includes a bare `{"type": "object"}`. Its class holds a namespace of those keys and values as given:

```apl
labels ← ⎕NEW models.Labels (colour: 'red' ⋄ size: 'L')
```
