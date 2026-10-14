using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Models;

namespace AudioRecorderTrayApp.Services
{
    public static class ClientPipeline
    {
        public sealed class Result
        {
            public List<SpeakerSegment> Segments { get; set; } = new();
            public string PlainText { get; set; } = string.Empty;
            public AnonymizationService.Mapping Mapping { get; set; } = new();
        }

        public static Task<Result> BuildAsync(IEnumerable<SpeakerSegment> therapistSegments, IEnumerable<SpeakerSegment> clientSegments, System.Threading.CancellationToken ct = default) => BuildAsync(therapistSegments, clientSegments, isAdminFlow: false, ct);
        public static async Task<Result> BuildAsync(IEnumerable<SpeakerSegment> therapistSegments, IEnumerable<SpeakerSegment> clientSegments, bool isAdminFlow, System.Threading.CancellationToken ct = default)
        {
            var merged = DiarizationService.MergeTimeline(therapistSegments, clientSegments);
            if (AppSettings.Current.NatashaWholeText)
            {
                var bulk = await AnonymizationService.AnonymizeWholeTextAsync(merged, ct).ConfigureAwait(false);
                return new Result
                {
                    Segments = bulk.segments,
                    PlainText = bulk.plainText,
                    Mapping = bulk.mapping
                };
            }
            else
            {
                var mapping = new AnonymizationService.Mapping();
                var anon = await AnonymizationService.AnonymizeSegmentsAsync(merged, mapping, ct).ConfigureAwait(false);
                var text = DialogueFormatter.ToPlainText(anon);
                return new Result
                {
                    Segments = anon,
                    PlainText = text,
                    Mapping = mapping
                };
            }
        }
    }
}
