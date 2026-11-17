package gitport;

import javax.swing.*;
import javax.swing.border.*;
import java.awt.*;
import java.nio.file.*;
import java.util.List;
import java.util.function.*;

/** Atom-styled modal dialog for creating a new Git Worktree. */
public final class NewWorktreeDialog extends JDialog {
    private final Git git;
    private final BiFunction<String, Path, Path> pickFolder;
    private final Consumer<Path> openRepo;
    private final Consumer<String> log;
    private final Consumer<String> error;

    private final JTextField pathInput = new JTextField();
    private final JRadioButton radioExisting = new JRadioButton("Checkout existing branch:");
    private final JComboBox<String> existingCombo;
    private final JRadioButton radioNew = new JRadioButton("Create new branch from base ref:");
    private final JTextField newBranchInput = new JTextField();
    private final JComboBox<String> baseRefCombo;
    private final JCheckBox switchNow = new JCheckBox("Switch GitPort to the new worktree immediately", true);

    public static void show(
        Frame parent,
        Git git,
        String baseRef,
        List<String> branches,
        BiFunction<String, Path, Path> pickFolder,
        Consumer<Path> openRepo,
        Consumer<String> log,
        Consumer<String> error
    ) {
        NewWorktreeDialog d = new NewWorktreeDialog(parent, git, baseRef, branches, pickFolder, openRepo, log, error);
        d.setVisible(true);
    }

    private NewWorktreeDialog(
        Frame parent,
        Git git,
        String baseRef,
        List<String> branches,
        BiFunction<String, Path, Path> pickFolder,
        Consumer<Path> openRepo,
        Consumer<String> log,
        Consumer<String> error
    ) {
        super(parent, "New Git Worktree", true);
        this.git = git;
        this.pickFolder = pickFolder;
        this.openRepo = openRepo;
        this.log = log;
        this.error = error;

        getContentPane().setBackground(AtomTheme.TREE);
        setLayout(new BorderLayout(0, 16));
        ((JPanel)getContentPane()).setBorder(new EmptyBorder(18, 20, 18, 20));

        // Header
        JPanel heading = new JPanel(new GridLayout(0, 1, 0, 4));
        heading.setOpaque(false);
        JLabel title = new JLabel("Create linked Git Worktree");
        title.setFont(AtomTheme.UI.deriveFont(Font.BOLD, 17f));
        title.setForeground(AtomTheme.BRIGHT);
        heading.add(title);
        JLabel subtitle = new JLabel("Allows working on another branch in a separate folder simultaneously.");
        subtitle.setFont(AtomTheme.UI.deriveFont(12f));
        subtitle.setForeground(AtomTheme.SECONDARY);
        heading.add(subtitle);
        add(heading, BorderLayout.NORTH);

        // Body form
        JPanel form = new JPanel();
        form.setLayout(new BoxLayout(form, BoxLayout.Y_AXIS));
        form.setOpaque(false);

        // 1. Location path
        JLabel pathLabel = new JLabel("Worktree Directory (location on disk):");
        pathLabel.setFont(AtomTheme.UI.deriveFont(Font.BOLD, 12f));
        pathLabel.setForeground(AtomTheme.TEXT);
        pathLabel.setAlignmentX(Component.LEFT_ALIGNMENT);
        form.add(pathLabel);
        form.add(Box.createVerticalStrut(6));

        JPanel pathRow = new JPanel(new BorderLayout(8, 0));
        pathRow.setOpaque(false);
        pathRow.setAlignmentX(Component.LEFT_ALIGNMENT);
        pathRow.setMaximumSize(new Dimension(Integer.MAX_VALUE, 32));

        String safeSlug = (baseRef != null && !baseRef.isBlank() ? baseRef : "worktree-1")
            .replace('/', '-').replace('\\', '-').replaceAll("[^a-zA-Z0-9_.-]", "");
        Path defaultLocation = git != null && git.repo != null && git.repo.getParent() != null
            ? git.repo.getParent().resolve(git.repo.getFileName().toString() + "-" + safeSlug)
            : Path.of("worktrees/" + safeSlug);
        pathInput.setText(defaultLocation.toAbsolutePath().normalize().toString());
        pathInput.setFont(AtomTheme.UI.deriveFont(12f));
        pathRow.add(pathInput, BorderLayout.CENTER);

        JButton browseBtn = new JButton("Browse...");
        AtomTheme.button(browseBtn, false);
        browseBtn.addActionListener(e -> {
            Path current = null;
            try { current = Path.of(pathInput.getText().strip()); } catch (Exception ignored) {}
            Path chosen = pickFolder.apply("Select Worktree Location", current != null ? current.getParent() : (git != null ? git.repo.getParent() : null));
            if (chosen != null) {
                pathInput.setText(chosen.toAbsolutePath().toString());
            }
        });
        pathRow.add(browseBtn, BorderLayout.EAST);
        form.add(pathRow);
        form.add(Box.createVerticalStrut(16));

        // 2. Branch selection
        JLabel branchHeading = new JLabel("Branch Configuration:");
        branchHeading.setFont(AtomTheme.UI.deriveFont(Font.BOLD, 12f));
        branchHeading.setForeground(AtomTheme.TEXT);
        branchHeading.setAlignmentX(Component.LEFT_ALIGNMENT);
        form.add(branchHeading);
        form.add(Box.createVerticalStrut(6));

        ButtonGroup bg = new ButtonGroup();
        bg.add(radioNew);
        bg.add(radioExisting);
        radioNew.setOpaque(false);
        radioNew.setForeground(AtomTheme.TEXT);
        radioExisting.setOpaque(false);
        radioExisting.setForeground(AtomTheme.TEXT);

        // Branch combos
        String[] branchArray = branches != null ? branches.toArray(String[]::new) : new String[]{"main"};
        existingCombo = new JComboBox<>(branchArray);
        existingCombo.setFont(AtomTheme.UI.deriveFont(12f));
        if (baseRef != null && branches != null && branches.contains(baseRef)) {
            existingCombo.setSelectedItem(baseRef);
        }

        baseRefCombo = new JComboBox<>(branchArray);
        baseRefCombo.setFont(AtomTheme.UI.deriveFont(12f));
        if (baseRef != null && branches != null && branches.contains(baseRef)) {
            baseRefCombo.setSelectedItem(baseRef);
        }

        newBranchInput.setFont(AtomTheme.UI.deriveFont(12f));
        newBranchInput.setText(baseRef != null && !baseRef.isBlank() ? "worktree-" + safeSlug : "feature/worktree");

        // Radio New Panel
        radioNew.setSelected(true);
        radioNew.setAlignmentX(Component.LEFT_ALIGNMENT);
        form.add(radioNew);
        form.add(Box.createVerticalStrut(4));

        JPanel newBranchRow = new JPanel(new GridLayout(1, 2, 8, 0));
        newBranchRow.setOpaque(false);
        newBranchRow.setAlignmentX(Component.LEFT_ALIGNMENT);
        newBranchRow.setMaximumSize(new Dimension(Integer.MAX_VALUE, 32));
        newBranchRow.add(newBranchInput);
        newBranchRow.add(baseRefCombo);
        form.add(newBranchRow);
        form.add(Box.createVerticalStrut(12));

        // Radio Existing Panel
        radioExisting.setAlignmentX(Component.LEFT_ALIGNMENT);
        form.add(radioExisting);
        form.add(Box.createVerticalStrut(4));

        existingCombo.setAlignmentX(Component.LEFT_ALIGNMENT);
        existingCombo.setMaximumSize(new Dimension(Integer.MAX_VALUE, 32));
        form.add(existingCombo);
        form.add(Box.createVerticalStrut(16));

        // Switch now checkbox
        switchNow.setOpaque(false);
        switchNow.setForeground(AtomTheme.SECONDARY);
        switchNow.setAlignmentX(Component.LEFT_ALIGNMENT);
        form.add(switchNow);

        add(form, BorderLayout.CENTER);

        // Buttons
        JPanel buttonRow = new JPanel(new FlowLayout(FlowLayout.RIGHT, 8, 0));
        buttonRow.setOpaque(false);
        JButton cancelBtn = new JButton("Cancel");
        AtomTheme.button(cancelBtn, false);
        cancelBtn.addActionListener(e -> dispose());

        JButton createBtn = new JButton("Create Worktree");
        AtomTheme.button(createBtn, true);
        createBtn.addActionListener(e -> createWorktree());

        buttonRow.add(cancelBtn);
        buttonRow.add(createBtn);
        add(buttonRow, BorderLayout.SOUTH);

        AtomTheme.polish(getContentPane());
        setSize(560, 420);
        setLocationRelativeTo(parent);
    }

    private void createWorktree() {
        String pathStr = pathInput.getText().strip();
        if (pathStr.isBlank()) {
            JOptionPane.showMessageDialog(this, "Please enter a valid worktree directory.");
            return;
        }

        Path target = Path.of(pathStr).toAbsolutePath().normalize();
        if (Files.exists(target)) {
            try {
                if (Files.isDirectory(target) && Files.list(target).findAny().isPresent()) {
                    JOptionPane.showMessageDialog(this, "The target directory already exists and is not empty:\n" + target, "Directory Not Empty", JOptionPane.WARNING_MESSAGE);
                    return;
                }
            } catch (Exception ignored) {}
        }

        boolean createNew = radioNew.isSelected();
        String targetBranch;
        String baseRef;

        if (createNew) {
            targetBranch = newBranchInput.getText().strip();
            baseRef = (String) baseRefCombo.getSelectedItem();
            try {
                Git.validateRef(targetBranch);
            } catch (Exception ex) {
                JOptionPane.showMessageDialog(this, ex.getMessage(), "Invalid Branch Name", JOptionPane.ERROR_MESSAGE);
                return;
            }
        } else {
            targetBranch = (String) existingCombo.getSelectedItem();
            baseRef = null;
        }

        dispose();

        SwingUtilities.invokeLater(() -> {
            try {
                Git.Result res = git.worktreeAdd(target, targetBranch, baseRef, createNew);
                log.accept("Worktree created: " + target + " (" + targetBranch + ")\n" + res.text());
                if (switchNow.isSelected()) {
                    openRepo.accept(target);
                }
            } catch (Exception ex) {
                error.accept("Failed to create worktree: " + ex.getMessage());
                JOptionPane.showMessageDialog(null, "Could not create worktree:\n" + ex.getMessage(), "Worktree Error", JOptionPane.ERROR_MESSAGE);
            }
        });
    }
}
