using System.IO;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace NzbDrone.Core.Download.Clients.RSS
{
    public class TorrentRSSSettingsValidator : AbstractValidator<TorrentRSSSettings>
    {
        public TorrentRSSSettingsValidator()
        {
            RuleFor(c => c.RSSDirectory).IsValidPath();
            RuleFor(c => c.MaxItems).GreaterThan(0);
        }
    }

    public class TorrentRSSSettings : IProviderConfig
    {
        private static readonly TorrentRSSSettingsValidator Validator = new TorrentRSSSettingsValidator();

        public TorrentRSSSettings()
        {
            MaxItems = 200;
        }

        [FieldDefinition(1, Label = "RSS Feed Output Directory", Type = FieldType.Path, HelpText = "Directory in which to write sonarr.rss")]
        public string RSSDirectory { get; set; }

        [FieldDefinition(1, Label = "Maximum Number of Entries", Type = FieldType.Number)]
        public int MaxItems { get; set; }

        public string RSSFilePath => Path.Combine(RSSDirectory, "sonar.rss");

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
