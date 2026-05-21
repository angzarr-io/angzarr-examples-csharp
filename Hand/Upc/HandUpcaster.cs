// Hand domain upcaster — transforms old event versions to current during replay.
//
// The upcaster is generic: parameterized by domain name, not tied to hand
// specifically. The same pattern applies to any domain's schema evolution.
//
// Currently passthrough — register transformation functions as schema changes.

namespace Hand.Upc;

public static class HandUpcaster
{
    public static Angzarr.Client.UpcasterRouter CreateRouter()
    {
        return new Angzarr.Client.UpcasterRouter("hand");
        // Register transformations as schema evolves:
        // .On("CardsDealtV1", old => {
        //     var v1 = old.Unpack<CardsDealtV1>();
        //     return Any.Pack(new CardsDealt { ... });
        // });
    }
}
