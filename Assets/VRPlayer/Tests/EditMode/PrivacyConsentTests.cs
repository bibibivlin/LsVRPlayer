using System;
using System.IO;
using NUnit.Framework;

namespace VRPlayer.Tests
{
    public sealed class PrivacyConsentTests
    {
        private string directory,path;
        [SetUp] public void Setup(){directory=Path.Combine(Path.GetTempPath(),"vrplayer-privacy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);path=Path.Combine(directory,"consent.json");}
        [TearDown] public void Cleanup(){Directory.Delete(directory,true);}
        [Test] public void FirstUseDoesNotImplyConsent(){Assert.That(new PrivacyConsentStore(path).Accepted,Is.False);Assert.That(File.Exists(path),Is.False);}
        [Test] public void ExplicitAcceptanceSurvivesRestart(){new PrivacyConsentStore(path).Accept();Assert.That(new PrivacyConsentStore(path).Accepted,Is.True);Assert.That(File.Exists(path+".tmp"),Is.False);}
        [TestCase("{broken")]
        [TestCase("{}")]
        [TestCase("{\"version\":\"old-policy\"}")]
        public void InvalidOrOldConfirmationRequiresNewChoice(string json){File.WriteAllText(path,json);Assert.That(new PrivacyConsentStore(path).Accepted,Is.False);}
        [Test] public void ConsentDoesNotModifyPlaybackSettings(){string settings=Path.Combine(directory,"settings.json");File.WriteAllText(settings,"unchanged");new PrivacyConsentStore(path).Accept();Assert.That(File.ReadAllText(settings),Is.EqualTo("unchanged"));}
        [Test] public void IncompleteCurrentConfirmationIsNotAcceptance(){File.WriteAllText(path,"{\"version\":\""+PrivacyConsentStore.PolicyVersion+"\"}");Assert.That(new PrivacyConsentStore(path).Accepted,Is.False);}
    }
}
