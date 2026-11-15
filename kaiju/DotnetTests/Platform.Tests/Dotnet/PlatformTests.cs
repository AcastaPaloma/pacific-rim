using System;
using System.Collections.Generic;
using NUnit.Framework;
using Game.Net;
using Game.Platform;
using Game.Platform.Null;

namespace Game.Platform.Tests
{
    public class PlatformTests
    {
        static NullPlatformServices User(NullPlatformHub hub, ulong id) =>
            new NullPlatformServices(new PlatformUserId(id), "P" + id, hub);

        [Test]
        public void Version_And_Kind()
        {
            Assert.AreEqual("platform@v1", PlatformContractVersions.Id);
            var p = new NullPlatformServices();
            Assert.AreEqual(PlatformKind.Null, p.Kind);
            Assert.IsTrue(p.IsAvailable);
            Assert.IsTrue(p.LocalUserId.IsValid);
            Assert.AreEqual(NullPlatformServices.DefaultDisplayName, p.LocalDisplayName);
        }

        [Test]
        public void Invalid_User_Or_Duplicate_Throws()
        {
            var hub = new NullPlatformHub();
            Assert.Throws<ArgumentException>(() => new NullPlatformServices(default, "x", hub));
            User(hub, 5);
            Assert.Throws<ArgumentException>(() => User(hub, 5));
        }

        [TestCase(1)] [TestCase(2)] [TestCase(4)] [TestCase(17)]
        public void Lobby_FillsToN_ThenFull(int n)
        {
            var hub = new NullPlatformHub();
            var host = User(hub, 1);
            Assert.AreEqual(LobbyResult.Ok, host.CreateLobby(n, out var lobby));
            for (ulong i = 2; i <= (ulong)n; i++) Assert.AreEqual(LobbyResult.Ok, User(hub, i).JoinLobby(lobby));
            Assert.AreEqual(LobbyResult.Full, User(hub, 1000).JoinLobby(lobby));
            var members = new List<LobbyMember>();
            Assert.AreEqual(LobbyResult.Ok, host.GetMembers(lobby, members));
            Assert.AreEqual(n, members.Count);
            Assert.IsTrue(members[0].IsOwner);
            for (int i = 0; i < n; i++) Assert.AreEqual((ulong)(i + 1), members[i].UserId.Value);
        }

        [Test]
        public void Lobby_ArgumentAndStateErrors()
        {
            var hub = new NullPlatformHub();
            var a = User(hub, 1); var b = User(hub, 2);
            Assert.AreEqual(LobbyResult.InvalidArgument, a.CreateLobby(0, out _));
            Assert.AreEqual(LobbyResult.InvalidArgument, a.CreateLobby(PlatformContractVersions.MaxLobbyMembers + 1, out _));
            Assert.AreEqual(LobbyResult.NotInLobby, a.LeaveLobby());
            Assert.AreEqual(LobbyResult.NotFound, a.JoinLobby(new LobbyId(99)));
            Assert.AreEqual(LobbyResult.InvalidArgument, a.JoinLobby(default));
            a.CreateLobby(2, out var l);
            Assert.AreEqual(LobbyResult.AlreadyInLobby, a.CreateLobby(2, out _));
            Assert.AreEqual(LobbyResult.AlreadyInLobby, a.JoinLobby(l));
            Assert.AreEqual(l, a.CurrentLobby);
            Assert.AreEqual(LobbyResult.Ok, b.JoinLobby(l));
            Assert.AreEqual(LobbyResult.NotFound, b.GetMembers(new LobbyId(99), new List<LobbyMember>()));
        }

        [Test]
        public void Owner_Leaves_OwnershipMigrates_EmptyLobbyDisappears()
        {
            var hub = new NullPlatformHub();
            var a = User(hub, 1); var b = User(hub, 2); var c = User(hub, 3);
            a.CreateLobby(3, out var l); b.JoinLobby(l); c.JoinLobby(l);
            Assert.AreEqual(LobbyResult.Ok, a.LeaveLobby());
            Assert.IsFalse(a.CurrentLobby.IsValid);
            Assert.AreEqual(LobbyResult.Ok, c.TryGetLobby(l, out var info));
            Assert.AreEqual(2UL, info.Owner.Value);
            Assert.AreEqual(2, info.MemberCount);
            b.LeaveLobby(); c.LeaveLobby();
            Assert.AreEqual(LobbyResult.NotFound, c.TryGetLobby(l, out _));
        }

        [Test]
        public void ListLobbies_JoinableOnly_AscendingIds()
        {
            var hub = new NullPlatformHub();
            var a = User(hub, 1); var b = User(hub, 2); var c = User(hub, 3); var d = User(hub, 4);
            a.CreateLobby(1, out var full); b.CreateLobby(4, out var open1); c.CreateLobby(2, out var open2);
            var list = new List<LobbyInfo>();
            d.ListLobbies(list);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(open1, list[0].Id); Assert.AreEqual(open2, list[1].Id);
            Assert.Less(full.Value, open1.Value);
        }

        [Test]
        public void Lobby_Ids_AreDeterministic()
        {
            ulong Run()
            {
                var hub = new NullPlatformHub();
                User(hub, 1).CreateLobby(2, out _);
                User(hub, 2).CreateLobby(2, out var l2);
                return l2.Value;
            }
            Assert.AreEqual(Run(), Run());
            Assert.AreEqual(2UL, Run());
        }

        [Test]
        public void P2P_PairedEnds_DeliverInOrder_BothDirections()
        {
            var hub = new NullPlatformHub();
            var a = User(hub, 1); var b = User(hub, 2);
            var ta = a.CreateP2PTransport(b.LocalUserId);
            Assert.IsNotNull(ta);
            var tb = b.CreateP2PTransport(a.LocalUserId);
            Assert.IsNotNull(tb);
            Assert.AreNotSame(ta, tb);
            Assert.IsTrue(ta.IsConnected && tb.IsConnected);
            Assert.IsTrue(ta.Send(NetChannel.Reliable, new byte[] { 1, 2 }));
            Assert.IsTrue(ta.Send(NetChannel.Unreliable, new byte[] { 3 }));
            Assert.IsTrue(tb.Send(NetChannel.Reliable, new byte[] { 9 }));
            Assert.IsTrue(tb.TryReceive(out var ch, out var p)); Assert.AreEqual(NetChannel.Reliable, ch); CollectionAssert.AreEqual(new byte[] { 1, 2 }, p);
            Assert.IsTrue(tb.TryReceive(out ch, out p)); Assert.AreEqual(NetChannel.Unreliable, ch); CollectionAssert.AreEqual(new byte[] { 3 }, p);
            Assert.IsFalse(tb.TryReceive(out _, out _));
            Assert.IsTrue(ta.TryReceive(out ch, out p)); CollectionAssert.AreEqual(new byte[] { 9 }, p);
        }

        [Test]
        public void P2P_SendCopiesPayload()
        {
            var hub = new NullPlatformHub();
            var a = User(hub, 1); var b = User(hub, 2);
            var ta = a.CreateP2PTransport(b.LocalUserId); var tb = b.CreateP2PTransport(a.LocalUserId);
            var buf = new byte[] { 7, 7 };
            ta.Send(NetChannel.Reliable, buf); buf[0] = 0;
            tb.TryReceive(out _, out var p);
            Assert.AreEqual(7, p[0]);
        }

        [Test]
        public void P2P_Close_DisconnectsBoth_QueuedStillReadable()
        {
            var hub = new NullPlatformHub();
            var a = User(hub, 1); var b = User(hub, 2);
            var ta = a.CreateP2PTransport(b.LocalUserId); var tb = b.CreateP2PTransport(a.LocalUserId);
            ta.Send(NetChannel.Reliable, new byte[] { 1 });
            ta.Close();
            Assert.IsFalse(ta.IsConnected); Assert.IsFalse(tb.IsConnected);
            Assert.IsFalse(tb.Send(NetChannel.Reliable, new byte[] { 2 }));
            Assert.IsTrue(tb.TryReceive(out _, out var p)); Assert.AreEqual(1, p[0]);
        }

        [Test]
        public void P2P_InvalidOrSelfPeer_ReturnsNull()
        {
            var a = new NullPlatformServices();
            Assert.IsNull(a.CreateP2PTransport(default));
            Assert.IsNull(a.CreateP2PTransport(a.LocalUserId));
        }

        [Test]
        public void P2P_ManyPeers_IndependentLinks()
        {
            var hub = new NullPlatformHub();
            var host = User(hub, 1);
            var clients = new List<NullPlatformServices>();
            var hostEnds = new List<ITransport>(); var clientEnds = new List<ITransport>();
            for (ulong i = 2; i <= 4; i++)
            {
                var c = User(hub, i); clients.Add(c);
                hostEnds.Add(host.CreateP2PTransport(c.LocalUserId));
                clientEnds.Add(c.CreateP2PTransport(host.LocalUserId));
            }
            for (int i = 0; i < 3; i++) hostEnds[i].Send(NetChannel.Reliable, new[] { (byte)i });
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(clientEnds[i].TryReceive(out _, out var p)); Assert.AreEqual(i, p[0]);
                Assert.IsFalse(clientEnds[i].TryReceive(out _, out _));
            }
        }
    }
}
