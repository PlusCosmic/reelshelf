import { createFileRoute } from "@tanstack/react-router";
import {
  LegalContact,
  LegalPage,
  legalOperator,
} from "@/components/Reelshelf/LegalPage";

export const Route = createFileRoute("/privacy")({
  component: PrivacyRoute,
});

function PrivacyRoute() {
  return (
    <LegalPage title="Privacy policy" updated="2 October 2026">
      <p>
        This explains what Reelshelf, run by {legalOperator}, collects, why, and
        who else handles it. We don't sell your data, show ads or use tracking
        or analytics scripts.
      </p>

      <h2>What we collect</h2>
      <ul>
        <li>
          <strong>From Discord or Twitch when you sign in:</strong> your account
          ID, username, display name, avatar, and email address if verified. You
          can change or clear the email in Settings.
        </li>
        <li>
          <strong>Twitch access tokens</strong>, stored encrypted, so we can
          list and import your own Twitch clips when you ask us to.
        </li>
        <li>
          <strong>What you add:</strong> clips and their titles, tags, games,
          collections, collaborators and share links.
        </li>
        <li>
          <strong>Usage within Reelshelf:</strong> which clips you've watched,
          so we can mark them as seen.
        </li>
        <li>
          <strong>Server logs:</strong> our hosting keeps standard request logs
          (such as IP address and browser) for security and debugging.
        </li>
      </ul>

      <h2>Cookies and local storage</h2>
      <p>
        We use one cookie to keep you signed in, and your browser's local
        storage to remember your light or dark theme. Nothing is used for
        advertising or tracking.
      </p>

      <h2>Automated analysis of clips</h2>
      <ul>
        <li>
          <strong>Apex Legends detection:</strong> for Apex clips, still frames
          are sent to OpenAI to recognise which legend you're playing, so clips
          can be sorted by legend.
        </li>
        <li>
          <strong>Speech transcription:</strong> for some accounts, a clip's
          audio is sent through OpenRouter to a speech-to-text provider (such as
          Google or Deepgram) to make clips searchable. Transcripts are used for
          search and may be reviewed by us to check their quality. Email us if
          you'd like your clips left out of transcription.
        </li>
      </ul>
      <p>
        These providers receive the data only to return a result to us, and
        handle it under their own API terms.
      </p>

      <h2>Who else handles your data</h2>
      <ul>
        <li>
          <strong>Bunny.net</strong> stores and streams your clips.
        </li>
        <li>
          <strong>Discord and Twitch</strong> handle sign-in, Twitch clip import
          and the Discord Activity.
        </li>
        <li>
          <strong>Resend</strong> delivers account emails, such as a new sign-in
          being linked or your storage being nearly full.
        </li>
        <li>
          <strong>OpenAI and OpenRouter</strong> (and the providers behind it)
          for the analysis above.
        </li>
        <li>
          <strong>Our hosting provider</strong> runs the servers and database.
        </li>
      </ul>
      <p>
        We share data only to run Reelshelf, or when the law requires us to.
      </p>

      <h2>Where your data is stored</h2>
      <p>
        Reelshelf is run from the UK. Our servers, database and clip storage are
        in Germany. Discord, Twitch, OpenAI, OpenRouter and Resend are based in
        the US, so data they handle leaves the UK and EU. We rely on their
        standard data protection terms, such as standard contractual clauses or
        the UK–US data bridge, to protect it.
      </p>

      <h2>Sharing and visibility</h2>
      <p>
        Your clips are visible only to you, to collaborators you add, to anyone
        with a share link to the clip or a shared collection, and to the people
        in a Discord watch room when you play a clip there. Watch rooms aren't
        saved; they end when everyone leaves. Clip video addresses aren't
        secret-signed, so treat clips as unlisted rather than private. A share
        link keeps working until you delete the clip.
      </p>

      <h2>How long we keep it</h2>
      <p>
        We keep your data while your account exists. Deleting a clip removes the
        video from storage and deletes its tags, detections and transcripts.
        Unlinking a sign-in method deletes its stored tokens. You can delete
        your whole account in Settings: it stops working straight away, and its
        clips are removed from storage and the rest of its data deleted shortly
        after, usually within minutes. Collections you made go with it,
        including for their collaborators. Backups and logs age out on their own
        schedule.
      </p>

      <h2>Your rights</h2>
      <p>
        Under UK and EU data protection law (UK GDPR and GDPR), you can ask for
        a copy of your data, a correction, deletion, or for us to stop or limit
        processing. We process your data to provide the service you signed up
        for and, for security logs and automated clip analysis, for our
        legitimate interests in running and improving Reelshelf. You can
        complain to the UK Information Commissioner's Office (ico.org.uk) or
        your local data protection authority.
      </p>

      <h2>Children</h2>
      <p>
        Reelshelf isn't meant for children under 13, or under the minimum age
        for Discord or Twitch in your country.
      </p>

      <h2>Changes and contact</h2>
      <p>
        We'll change the date at the top when this policy changes, and email you
        about significant changes. For questions or requests, email{" "}
        <LegalContact />.
      </p>
    </LegalPage>
  );
}
