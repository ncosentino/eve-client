"""Structural tests for documentation publication ownership."""

from pathlib import Path
import unittest

import yaml


class DocumentationWorkflowTests(unittest.TestCase):
    """Verify release preparation cannot win the Pages deployment race."""

    @classmethod
    def setUpClass(cls) -> None:
        workflow_path = (
            Path(__file__).parents[2] / ".github" / "workflows" / "docs.yml"
        )
        cls.workflow = workflow_path.read_text(encoding="utf-8-sig")
        cls.jobs = yaml.safe_load(cls.workflow)["jobs"]

    def test_release_preparation_detection_uses_the_commit_subject(self) -> None:
        """The push gate recognizes the conventional release commit prefix."""
        self.assertIn(
            "github.event_name == 'push'",
            self.workflow,
        )
        self.assertIn(
            "startsWith(github.event.head_commit.message, "
            "'chore(release): prepare ')",
            self.workflow,
        )

    def test_release_preparation_skips_every_publication_entry_point(self) -> None:
        """Neither artifact upload nor the publish job may run for that push."""
        build = self.jobs["build"]
        self.assertEqual(
            build["outputs"]["publish_site"],
            "${{ steps.publication.outputs.publish_site }}",
        )
        self.assertIn(
            'echo "publish_site=false" >> "$GITHUB_OUTPUT"',
            self.workflow,
        )
        self.assertIn(
            'echo "publish_site=true" >> "$GITHUB_OUTPUT"',
            self.workflow,
        )
        upload = next(
            step
            for step in build["steps"]
            if step.get("name") == "Upload documentation site"
        )
        self.assertIn(
            "steps.publication.outputs.publish_site == 'true'",
            upload["if"],
        )

        publish = self.jobs["publish"]
        self.assertIn(
            "needs.build.outputs.publish_site == 'true'",
            publish["if"],
        )

        deploy = self.jobs["deploy"]
        self.assertEqual(deploy["needs"], ["build", "publish"])
        self.assertIn(
            "needs.build.outputs.publish_site == 'true'",
            deploy["if"],
        )


if __name__ == "__main__":
    unittest.main()
