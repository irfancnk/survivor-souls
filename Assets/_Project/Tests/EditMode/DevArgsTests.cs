using NUnit.Framework;
using SurvivorSouls.Dev;

namespace SurvivorSouls.Tests
{
    /// <summary>KAN-179: dev helpers read the command line, or the SS_ARGS environment variable when it has no -ss- argument.</summary>
    public class DevArgsTests
    {
        [Test]
        public void CommandLineWithSsArgs_WinsOverEnv()
        {
            string[] line = { "/app/Survivor Souls", "-ss-scene", "Game", "-ss-immortal" };
            string[] all = DevArgs.Read(line, "-ss-scene Boss", out bool fromEnv);

            Assert.That(fromEnv, Is.False);
            Assert.That(all, Is.SameAs(line));
        }

        [Test]
        public void NoSsArgs_UsesEnv_SplitOnWhitespace_KeepsExe()
        {
            string[] line = { "/app/SurvivorSouls", "-batchmode" };
            string[] all = DevArgs.Read(line, "  -ss-scene Game\t-ss-horde 60  -ss-immortal ", out bool fromEnv);

            Assert.That(fromEnv, Is.True);
            Assert.That(all, Is.EqualTo(new[] { "/app/SurvivorSouls", "-ss-scene", "Game", "-ss-horde", "60", "-ss-immortal" }));
        }

        [Test]
        public void NoSsArgs_NoEnv_KeepsCommandLine()
        {
            string[] line = { "/app/SurvivorSouls" };

            Assert.That(DevArgs.Read(line, null, out bool a), Is.SameAs(line));
            Assert.That(DevArgs.Read(line, "   ", out bool b), Is.SameAs(line));
            Assert.That(a || b, Is.False);
        }

        [Test]
        public void ExePathIsNotAnArgument()
        {
            // Element 0 is the executable, even if its name starts with -ss-.
            string[] all = DevArgs.Read(new[] { "-ss-odd-exe-name" }, "-ss-scene Game", out bool fromEnv);

            Assert.That(fromEnv, Is.True);
            Assert.That(all, Is.EqualTo(new[] { "-ss-odd-exe-name", "-ss-scene", "Game" }));
            Assert.That(DevArgs.Read(null, null, out _), Is.Empty);
        }
    }
}
