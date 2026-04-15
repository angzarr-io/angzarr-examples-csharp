// Player domain upcaster — transforms old event versions to current during replay.
//
// The upcaster is generic: parameterized by domain name, not tied to player
// specifically. The same pattern applies to any domain's schema evolution.
//
// Currently passthrough — register transformation functions as schema changes.

namespace Player.Upc;

public static class PlayerUpcaster
{
    public static Angzarr.Client.UpcasterRouter CreateRouter()
    {
        return new Angzarr.Client.UpcasterRouter("player");
        // Register transformations as schema evolves:
        // .On("PlayerRegisteredV1", old => {
        //     var v1 = old.Unpack<PlayerRegisteredV1>();
        //     return Any.Pack(new PlayerRegistered {
        //         DisplayName = v1.DisplayName,
        //         Email = v1.Email,
        //     });
        // });
    }
}
