import { Router } from 'express';
import { z } from 'zod';
import { AuthService } from './auth.service.js';
import { requireAuth } from './auth.middleware.js';

const router = Router();

// POST /auth/register
router.post('/register', async (req, res) => {
  const schema = z.object({
    username: z.string().min(2).max(20).regex(/^[a-zA-Z0-9_]+$/),
    email: z.string().email(),
    password: z.string().min(8),
  });
  const parsed = schema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ error: parsed.error.errors });
    return;
  }
  try {
    const result = await AuthService.register(
      parsed.data.username,
      parsed.data.email,
      parsed.data.password,
    );
    res.status(201).json(result);
  } catch (err: any) {
    res.status(409).json({ error: err.message });
  }
});

// POST /auth/login
router.post('/login', async (req, res) => {
  const schema = z.object({
    emailOrUsername: z.string(),
    password: z.string(),
  });
  const parsed = schema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ error: parsed.error.errors });
    return;
  }
  try {
    const result = await AuthService.login(parsed.data.emailOrUsername, parsed.data.password);
    res.json(result);
  } catch {
    res.status(401).json({ error: 'Invalid credentials' });
  }
});

// POST /auth/guest — get a guest JWT (used by Unity clients on first connect)
router.post('/guest', async (req, res) => {
  const schema = z.object({ displayName: z.string().min(2).max(20).optional() });
  const parsed = schema.safeParse(req.body);
  const displayName = parsed.success ? parsed.data.displayName : undefined;
  try {
    const result = await AuthService.createGuest(displayName);
    res.status(201).json(result);
  } catch (err: any) {
    res.status(500).json({ error: err.message });
  }
});

// POST /auth/refresh
router.post('/refresh', async (req, res) => {
  const { refreshToken } = req.body;
  if (!refreshToken) {
    res.status(400).json({ error: 'refreshToken required' });
    return;
  }
  try {
    const tokens = await AuthService.refresh(refreshToken);
    res.json(tokens);
  } catch (err: any) {
    res.status(401).json({ error: err.message });
  }
});

// DELETE /auth/logout
router.delete('/logout', requireAuth, async (req, res) => {
  const { refreshToken } = req.body;
  if (refreshToken) await AuthService.logout(refreshToken);
  res.status(204).send();
});

export { router as authRouter };
