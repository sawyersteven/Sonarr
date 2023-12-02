using System.ComponentModel;
using System.IO;
using FluentValidation;
using Newtonsoft.Json;
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
            ReadOnly = true;
        }

        [FieldDefinition(1, Label = "TorrentRSSFeedOutputDirectory", Type = FieldType.Path, HelpText = "TorrentRSSOutputDirectoryHelpText")]
        public string RSSDirectory { get; set; }

        [FieldDefinition(1, Label = "TorrentRSSWatchFolder", Type = FieldType.Path, HelpText = "TorrentRSSWatchFolderHelpText")]
        public string WatchFolder { get; set; }

        [DefaultValue(200)]
        [FieldDefinition(1, Label = "MaximumNumberofRSSEntries", Type = FieldType.Number)]
        public int MaxItems { get; set; }

        public string RSSFilePath => Path.Combine(RSSDirectory, "sonar.rss");

        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [FieldDefinition(4, Label = "TorrentRSSFilesReadOnly", Type = FieldType.Checkbox, HelpText = "TorrentRSSFilesReadOnlyHelpText")]
        public bool ReadOnly { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
