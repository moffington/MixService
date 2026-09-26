using S1API.Entities;
using S1API.Messaging;

namespace MixService.NPCs;

public sealed class MixerContactNpc : NPC
{
    public static MixerContactNpc? Instance { get; private set; }

    public override bool IsPhysical => false;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        builder
            .WithIdentity(
                id: "mixservice:milo",
                firstName: "Milo",
                lastName: "")
            .WithIcon(null);
    }

    protected override void OnCreated()
    {
        base.OnCreated();

        Instance = this;

        var acknowledgement = new Response
        {
            Label = "mixservice_test_ack",
            Text = "yeah"
        };

        acknowledgement.OnTriggered = () =>
        {
            Messaging.SendTextMessage(
                "good.",
                responses: null,
                responseDelay: 0f);
        };

        Messaging.SendTextMessage(
            "yo. testing this thing. you getting this?",
            new[] { acknowledgement });
    }

    protected override void OnDestroyed()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;

        base.OnDestroyed();
    }
}