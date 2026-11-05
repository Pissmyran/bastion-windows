# Issues

GitHub Issues in `pissmyran/bastion-windows` is the repo's issue tracker.

## Working with issues

- Read the whole issue before working on it: text, labels, comments and linked pull requests.
- An issue should describe one coherent problem, with acceptance criteria and blockers in the text.
- Decisions that should last go into the documentation, not only into a comment.

## From a cloud session

`gh issue` does not work in cloud sessions (GraphQL is disabled). Use REST:

Set the issue number once, replacing `1` with the real number:

```bash
ISSUE=1
```

Read the issue:

```bash
gh api "repos/pissmyran/bastion-windows/issues/$ISSUE"
```

Read its comments:

```bash
gh api "repos/pissmyran/bastion-windows/issues/$ISSUE/comments"
```

List open issues:

```bash
gh api 'repos/pissmyran/bastion-windows/issues?state=open'
```

Create an issue, replacing `TITLE` and `BODY`:

```bash
gh api repos/pissmyran/bastion-windows/issues -f title='TITLE' -f body='BODY'
```

Comment on an issue, replacing `BODY`:

```bash
gh api "repos/pissmyran/bastion-windows/issues/$ISSUE/comments" -f body='BODY'
```

Close an issue:

```bash
gh api -X PATCH "repos/pissmyran/bastion-windows/issues/$ISSUE" -f state=closed
```
