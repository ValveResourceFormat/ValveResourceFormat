using System;
using System.Collections.Generic;
using NUnit.Framework;
using ValveResourceFormat.Utils;

namespace Tests
{
    public class StringTokenTest
    {
        [Test]
        public void EnsureUniqueStringToken()
        {
            var seen = new Dictionary<uint, string>();

            foreach (var key in StringToken.Lookup)
            {
                Assert.That(key.Key, Is.EqualTo(key.Key.ToLowerInvariant()));

                if (seen.ContainsKey(key.Value))
                {
                    Assert.Fail($"{key.Key} ({key.Value}) collides with {seen[key.Value]}");
                }

                seen[key.Value] = key.Key;
            }
        }
    }
}
