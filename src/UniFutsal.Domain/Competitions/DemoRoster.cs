using System;
using System.Collections.Generic;
using System.Linq;

namespace UniFutsal.Domain.Competitions
{
    public enum DemoPlayerRole
    {
        Goalkeeper,
        Defender,
        Winger,
        Pivot,
        Universal
    }

    public sealed class DemoPlayer
    {
        public string Id { get; }
        public string Name { get; }
        public string Club { get; }
        public DemoPlayerRole Role { get; }

        internal DemoPlayer(string id, string name, string club, DemoPlayerRole role)
        {
            Id = id;
            Name = name;
            Club = club;
            Role = role;
        }
    }

    public static class DemoRoster
    {
        public static IReadOnlyList<DemoPlayer> Create()
        {
            var slots = new (string Name, DemoPlayerRole Role)[]
            {
                ("Álvaro", DemoPlayerRole.Goalkeeper), ("Bruno", DemoPlayerRole.Goalkeeper),
                ("Carlos", DemoPlayerRole.Defender), ("Daniel", DemoPlayerRole.Defender),
                ("Eduardo", DemoPlayerRole.Defender), ("Felipe", DemoPlayerRole.Winger),
                ("Gabriel", DemoPlayerRole.Winger), ("Hugo", DemoPlayerRole.Winger),
                ("Iván", DemoPlayerRole.Winger), ("Javier", DemoPlayerRole.Pivot),
                ("Lucas", DemoPlayerRole.Pivot), ("Marcos", DemoPlayerRole.Pivot),
                ("Nicolás", DemoPlayerRole.Universal), ("Óscar", DemoPlayerRole.Universal)
            };
            var surnames = new[]
            {
                "Acosta", "Beltrán", "Campos", "Duarte", "Estévez", "Fuentes", "Galán", "Herrera",
                "Iglesias", "Lara", "Molina", "Navarro", "Ortega", "Pardo", "Rojas", "Soler"
            };
            var clubs = DemoCalendar.Create()
                .SelectMany(fixture => new[] { fixture.HomeClub, fixture.AwayClub })
                .Distinct(StringComparer.Ordinal)
                .OrderBy(club => club, StringComparer.Ordinal)
                .ToArray();

            var players = new List<DemoPlayer>(clubs.Length * slots.Length);
            for (var clubIndex = 0; clubIndex < clubs.Length; clubIndex++)
            {
                for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    var slot = slots[slotIndex];
                    var surname = surnames[(clubIndex * 2 + slotIndex) % surnames.Length];
                    var id = FormattableString.Invariant($"demo-{clubIndex + 1:D2}-{slotIndex + 1:D2}");
                    players.Add(new DemoPlayer(id, $"{slot.Name} {surname}", clubs[clubIndex], slot.Role));
                }
            }

            return players.AsReadOnly();
        }
    }
}
