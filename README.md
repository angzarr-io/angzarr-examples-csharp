# angzarr-examples-csharp

Example implementations demonstrating Angzarr event sourcing patterns in C#. See the [Angzarr documentation](https://angzarr.io/) for more information.

> The poker example has been retired. A blackjack example is coming; its spec lives in [angzarr-project](https://github.com/angzarr-io/angzarr-project) under `proto/io/angzarr/examples/v1` and `features/example/blackjack*`.

## Development

### Setup

Install git hooks (requires [lefthook](https://github.com/evilmartians/lefthook)):

```bash
lefthook install
```

### Recipes

```bash
just -l              # List all available recipes
just build           # Generate protos (buf) and build the solution
just test            # Run tests
just fmt             # Format code
```

## License

BSD-3-Clause
