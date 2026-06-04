using System;
using Content.Server.GameTicking;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Tests.Server.GameTicking;

[TestFixture]
public sealed class PersistentCharacterSavePathTest
{
    [Test]
    public void PlayerPathIsDeterministicAndSafe()
    {
        var userId = new NetUserId(Guid.Parse("34314e77-2dbb-4198-8755-953988ebaf09"));

        var path = PersistentCharacterSavePath.ForPlayer(userId).ToString();

        Assert.That(path, Is.EqualTo("/persistent-characters/34314e772dbb41988755953988ebaf09.yml"));
        Assert.That(path, Does.Not.Contain(" "));
        Assert.That(path, Does.Not.Contain("["));
        Assert.That(path, Does.Not.Contain("]"));
    }

    [Test]
    public void NpcPathDoesNotUseDisplayNames()
    {
        var path = PersistentCharacterSavePath.ForNpc(new EntityUid(42)).ToString();

        Assert.That(path, Is.EqualTo("/persistent-characters/npc/42.yml"));
        Assert.That(path, Does.Not.Contain(" "));
        Assert.That(path, Does.Not.Contain("["));
        Assert.That(path, Does.Not.Contain("]"));
    }
}
