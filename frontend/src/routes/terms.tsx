import { Link, createFileRoute } from "@tanstack/react-router";
import {
  LegalContact,
  LegalPage,
  legalGoverningLaw,
  legalOperator,
} from "@/components/Reelshelf/LegalPage";

export const Route = createFileRoute("/terms")({
  component: TermsRoute,
});

function TermsRoute() {
  return (
    <LegalPage title="Terms of service" updated="24 October 2026">
      <p>
        Reelshelf is a place to keep, sort and share your gaming clips. It is
        run by {legalOperator} as a small, independent project. By signing in or
        using Reelshelf you agree to these terms and to the{" "}
        <Link to="/privacy">privacy policy</Link>.
      </p>

      <h2>Your account</h2>
      <p>
        You sign in with Discord or Twitch, so you must meet their minimum age
        and follow their terms. You're responsible for what happens on your
        account. Each linked sign-in opens the same account.
      </p>

      <h2>Your clips</h2>
      <p>
        Everything you upload or import stays yours. To run the service you let
        us store your clips, convert them for streaming, make thumbnails, and
        show them to you and anyone you share them with. For some clips we also
        run automated analysis, such as recognising the Apex Legends character
        on screen or transcribing speech for search. The{" "}
        <Link to="/privacy">privacy policy</Link> explains how that works. This
        permission ends when you delete the clip.
      </p>
      <p>
        Share links and shared collections let anyone with the link watch those
        clips. Only share what you're happy for others to see.
      </p>

      <h2>What you can't do</h2>
      <ul>
        <li>
          Upload anything you don't have the right to share, or anything
          illegal, hateful, sexually explicit or meant to harass someone.
        </li>
        <li>
          Use Reelshelf as general file hosting, or try to get around your
          plan's limits.
        </li>
        <li>
          Interfere with the service, other people's accounts, or our providers'
          systems.
        </li>
      </ul>
      <p>We may remove content or suspend accounts that break these rules.</p>

      <h2>Plans and limits</h2>
      <p>
        Every account starts on the free plan, which keeps up to 100 clips of up
        to a minute each. The paid plans keep more and longer clips: Plus up to
        1,000 clips and Pro up to 5,000, each up to two minutes. Uploads are
        under a fair use limit: in any 30 days you can add up to twice the clips
        your plan keeps, even if you delete some along the way. Settings shows
        your plan and how much of it you use. Some accounts get more at our
        discretion. We may change these limits. If we lower one, we'll give you
        notice before anything you've already uploaded is affected.
      </p>

      <h2>Paid plans</h2>
      <p>
        Paid plans are sold to you by Stripe, through its Link service, as the
        merchant of record. Link takes your payment, charges any tax, sends your
        receipts and handles billing questions, under its own terms of sale,
        which also cover refunds and your right to cancel. Charges show on your
        statement as LINK.COM* REELSHELF. Nothing here takes away rights you
        have under consumer law.
      </p>
      <ul>
        <li>
          A plan renews automatically every month or year, at the price shown
          when you subscribed, until you cancel. We'll tell you before a price
          change applies to you.
        </li>
        <li>
          Cancel or change your plan any time in Settings, under Manage billing.
          A cancelled plan lasts until the end of the period you've paid for.
        </li>
        <li>
          If a payment fails, Stripe tries again for a while. If it still can't
          be taken, the plan ends.
        </li>
        <li>
          When a paid plan ends, your account goes back to the free plan, and
          you can't add clips while you have more than it keeps. The clips over
          its limit are kept for 14 days, so you can download them, pick which
          clips to keep, or subscribe again. After that they're deleted, and we
          email you a download of them that lasts 28 days. The clips kept are
          the ones you picked, then your newest.
        </li>
      </ul>

      <h2>No guarantees</h2>
      <p>
        Reelshelf is provided as is, without warranties of any kind. It may go
        down, change, or lose data, so keep your own copies of clips that matter
        to you. As far as the law allows, we aren't liable for indirect or
        consequential losses or for lost content. Nothing in these terms limits
        rights you have under consumer law that can't be excluded.
      </p>

      <h2>Leaving, and the service ending</h2>
      <p>
        You can stop using Reelshelf at any time and ask us to delete your
        account. If we shut Reelshelf down, we'll try to give you reasonable
        notice so you can download your clips first.
      </p>

      <h2>Changes</h2>
      <p>
        We may update these terms. We'll change the date at the top, and for
        significant changes we'll let you know by email or in the app.
        Continuing to use Reelshelf after a change means you accept it.
      </p>

      <h2>Law and contact</h2>
      <p>
        These terms are governed by the laws of {legalGoverningLaw}. Questions?
        Email <LegalContact />.
      </p>
    </LegalPage>
  );
}
