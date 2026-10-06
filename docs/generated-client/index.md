# Generated Client

The generator produces a self-contained Dyalog APL client in the output directory. The client is a `Client` class and the supporting files it needs to make HTTP requests — everything required to call the API from APL, with no extra dependencies beyond Dyalog itself.

## What is produced

Running the generator writes the following to your output directory:

```
<output>/
├── APLSource/
│   ├── Client.aplc           ⍝ Main client class
│   ├── HttpCommand.aplc      ⍝ Bundled HTTPCommand library
│   ├── utils.apln            ⍝ Internal utility functions
│   ├── Version.aplf          ⍝ Version string function
│   ├── _tags/
│   │   └── <tagName>/        ⍝ One sub-directory per API tag
│   │       └── <OperationId>.aplf   ⍝ One function per operation
│   └── models/
│       └── <Model>.aplc      ⍝ One class per schema
├── docs/
│   └── <tagName>.md          ⍝ Reference for each tag's operations
└── README.md                 ⍝ Quick start and an index of the API
```

The original spec file is also copied into the output directory alongside the `README.md`.

The `README.md` and the pages in `docs/` are written for the specific API: they show how to authenticate, and give a ready-to-run example of calling each operation with its parameters and request body. They are a good place to start when using a newly generated client.

## Organising principle

The structure mirrors the tag and operation layout of the OpenAPI spec:

- Each **tag** becomes a public field on the `Client` class, holding a namespace of that tag's operations. The field is named after the tag in camelCase: a `user` tag would produce `client.user`, and a `store orders` tag `client.storeOrders`.
- Each **operation** within a tag becomes a function in that namespace, named after the `operationId`. For example, `listUsers` or `list_users` would produce `client.user.ListUsers`.
- Operations that have no tag are grouped under a `default` namespace.
- Each object **schema** in the spec's `components` becomes a [model class](models.md) in the `models` namespace.

This means you always call an endpoint as `client.<tag>.<OperationId>`.

## Requirements

- **Dyalog APL v20.0 or later** — required to load and run the generated code
- **LINK** — used to load `APLSource/` into your workspace; bundled with Dyalog APL
